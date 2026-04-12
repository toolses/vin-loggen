using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Services;

// ── Public DTOs ─────────────────────────────────────────────────────────────
public record PlaceAutocompleteSuggestion(string PlaceId, string MainText, string SecondaryText);
public record PlaceDetailsResult(string PlaceId, string Name, string? Address, double Lat, double Lng, string[]? Types);

// ── Interface ───────────────────────────────────────────────────────────────
public interface IGooglePlacesService
{
    Task<List<PlaceAutocompleteSuggestion>> AutocompleteAsync(
        string query, string sessionToken, double? lat, double? lng, CancellationToken ct);

    Task<PlaceDetailsResult?> GetDetailsAsync(
        string placeId, string sessionToken, CancellationToken ct);
}

// ── Implementation ──────────────────────────────────────────────────────────

/// <summary>
/// Proxies Google Places API (New) and caches resolved places in the
/// <c>locations</c> table to minimize API costs.
///
/// Authentication: reads <c>GOOGLE_PLACES_API_KEY</c> from configuration.
/// HTTP resilience (retry + circuit breaker) is configured in Program.cs via
/// the "googlePlaces" named <see cref="IHttpClientFactory"/> registration.
/// </summary>
public sealed class GooglePlacesService : IGooglePlacesService
{
    // ── Google Places API (New) JSON shapes ─────────────────────────────────

    private sealed record AutocompleteRequest(
        [property: JsonPropertyName("input")]        string Input,
        [property: JsonPropertyName("sessionToken")]  string SessionToken,
        [property: JsonPropertyName("languageCode")]  string LanguageCode,
        [property: JsonPropertyName("regionCode")]    string RegionCode,
        [property: JsonPropertyName("locationBias")]  LocationBias? LocationBias = null
    );

    private sealed record LocationBias(
        [property: JsonPropertyName("circle")] CircleBias Circle
    );

    private sealed record CircleBias(
        [property: JsonPropertyName("center")] LatLng Center,
        [property: JsonPropertyName("radius")] double Radius
    );

    private sealed record LatLng(
        [property: JsonPropertyName("latitude")]  double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude
    );

    private sealed record AutocompleteResponse(
        [property: JsonPropertyName("suggestions")] List<SuggestionWrapper>? Suggestions
    );

    private sealed record SuggestionWrapper(
        [property: JsonPropertyName("placePrediction")] PlacePrediction? PlacePrediction
    );

    private sealed record PlacePrediction(
        [property: JsonPropertyName("placeId")]          string? PlaceId,
        [property: JsonPropertyName("structuredFormat")]  StructuredFormat? StructuredFormat,
        [property: JsonPropertyName("text")]              TextBlock? Text
    );

    private sealed record StructuredFormat(
        [property: JsonPropertyName("mainText")]      TextBlock? MainText,
        [property: JsonPropertyName("secondaryText")] TextBlock? SecondaryText
    );

    private sealed record TextBlock(
        [property: JsonPropertyName("text")] string? Text
    );

    private sealed record PlaceResponse(
        [property: JsonPropertyName("displayName")]      DisplayName? DisplayName,
        [property: JsonPropertyName("formattedAddress")] string? FormattedAddress,
        [property: JsonPropertyName("location")]         LocationData? Location,
        [property: JsonPropertyName("types")]            List<string>? Types
    );

    private sealed record DisplayName(
        [property: JsonPropertyName("text")] string? Text
    );

    private sealed record LocationData(
        [property: JsonPropertyName("latitude")]  double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude
    );

    // ── Fields ──────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IntegrationSettings _settings;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GooglePlacesService> _logger;
    private readonly IApiUsageService _apiUsage;

    private const string QuotaCacheKey = "google-places:request-count";
    private static readonly TimeSpan QuotaCacheTtl = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GooglePlacesService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IntegrationSettings settings,
        NpgsqlDataSource dataSource,
        IMemoryCache cache,
        ILogger<GooglePlacesService> logger,
        IApiUsageService apiUsage)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _settings = settings;
        _dataSource = dataSource;
        _cache = cache;
        _logger = logger;
        _apiUsage = apiUsage;
    }

    // ── Autocomplete ────────────────────────────────────────────────────────

    public async Task<List<PlaceAutocompleteSuggestion>> AutocompleteAsync(
        string query, string sessionToken, double? lat, double? lng, CancellationToken ct)
    {
        if (await IsQuotaExceededAsync(ct)) return [];

        var apiKey = _configuration["GOOGLE_PLACES_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("GooglePlacesService: GOOGLE_PLACES_API_KEY not configured, skipping");
            return [];
        }

        if (string.IsNullOrWhiteSpace(query)) return [];

        LocationBias? bias = null;
        if (lat.HasValue && lng.HasValue)
        {
            bias = new LocationBias(new CircleBias(
                new LatLng(lat.Value, lng.Value), 50000));
        }

        var body = new AutocompleteRequest(query, sessionToken, "no", "NO", bias);

        try
        {
            var client = _httpClientFactory.CreateClient("googlePlaces");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/places:autocomplete");
            request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOpts),
                System.Text.Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            _ = _apiUsage.LogAsync("google-places", "autocomplete",
                (int)response.StatusCode, (int)sw.ElapsedMilliseconds, null, ct);
            // Evict quota cache so the next check reads a fresh count from the DB.
            _cache.Remove(QuotaCacheKey);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GooglePlacesService: autocomplete HTTP {Status}", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<AutocompleteResponse>(json, JsonOpts);

            return (result?.Suggestions ?? [])
                .Where(s => s.PlacePrediction?.PlaceId is not null)
                .Select(s =>
                {
                    var p = s.PlacePrediction!;
                    return new PlaceAutocompleteSuggestion(
                        p.PlaceId!,
                        p.StructuredFormat?.MainText?.Text ?? p.Text?.Text ?? "",
                        p.StructuredFormat?.SecondaryText?.Text ?? "");
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "GooglePlacesService: autocomplete failed for '{Query}'", query);
            return [];
        }
    }

    // ── Place Details (with DB cache) ───────────────────────────────────────

    public async Task<PlaceDetailsResult?> GetDetailsAsync(
        string placeId, string sessionToken, CancellationToken ct)
    {
        // Cache check does NOT count against quota
        var cached = await GetCachedAsync(placeId, ct);
        if (cached is not null)
        {
            _logger.LogDebug("GooglePlacesService: cache hit for place_id '{PlaceId}'", placeId);
            return cached;
        }

        if (await IsQuotaExceededAsync(ct)) return null;

        // 2. Call Google Places API
        var apiKey = _configuration["GOOGLE_PLACES_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("GooglePlacesService: GOOGLE_PLACES_API_KEY not configured, skipping");
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("googlePlaces");
            var url = $"/v1/places/{placeId}?sessionToken={Uri.EscapeDataString(sessionToken)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
            request.Headers.TryAddWithoutValidation("X-Goog-FieldMask",
                "displayName,formattedAddress,location,types");

            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            _ = _apiUsage.LogAsync("google-places", "details",
                (int)response.StatusCode, (int)sw.ElapsedMilliseconds, null, ct);
            // Evict quota cache so the next check reads a fresh count from the DB.
            _cache.Remove(QuotaCacheKey);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GooglePlacesService: details HTTP {Status} for '{PlaceId}'",
                    response.StatusCode, placeId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var place = JsonSerializer.Deserialize<PlaceResponse>(json, JsonOpts);
            if (place?.Location is null)
            {
                _logger.LogWarning("GooglePlacesService: no location in response for '{PlaceId}'", placeId);
                return null;
            }

            var result = new PlaceDetailsResult(
                placeId,
                place.DisplayName?.Text ?? "",
                place.FormattedAddress,
                place.Location.Latitude,
                place.Location.Longitude,
                place.Types?.ToArray());

            // 3. Cache for future lookups
            await CacheAsync(result, ct);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "GooglePlacesService: details failed for '{PlaceId}'", placeId);
            return null;
        }
    }

    // ── DB cache helpers ────────────────────────────────────────────────────

    private async Task<PlaceDetailsResult?> GetCachedAsync(string placeId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            var row = await conn.QueryFirstOrDefaultAsync<VinLoggen.Api.Models.LocationRecord>(
                "SELECT * FROM locations WHERE google_place_id = @PlaceId",
                new { PlaceId = placeId });

            if (row is null) return null;

            return new PlaceDetailsResult(
                row.GooglePlaceId, row.Name, row.Address, row.Lat, row.Lng, row.Types);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GooglePlacesService: cache lookup failed");
            return null;
        }
    }

    private async Task CacheAsync(PlaceDetailsResult place, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO locations (google_place_id, name, address, lat, lng, types)
                VALUES (@PlaceId, @Name, @Address, @Lat, @Lng, @Types)
                ON CONFLICT (google_place_id) DO NOTHING
                """,
                new { place.PlaceId, place.Name, place.Address, place.Lat, place.Lng, place.Types });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GooglePlacesService: cache insert failed for '{PlaceId}'", place.PlaceId);
        }
    }

    // ── Quota guard ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> and logs a critical warning when the total number of
    /// Google Places API calls recorded in <c>api_usage_logs</c> has reached or
    /// exceeded <see cref="IntegrationSettings.GooglePlacesMaxRequests"/>.
    /// The count is cached for <see cref="QuotaCacheTtl"/> to avoid a DB round-trip
    /// on every keystroke; the cache is also evicted immediately after each real
    /// API call so the check stays as current as possible.
    /// </summary>
    private async Task<bool> IsQuotaExceededAsync(CancellationToken ct)
    {
        var max = _settings.GooglePlacesMaxRequests;
        if (max <= 0) return false; // 0 = unlimited

        if (!_cache.TryGetValue<long>(QuotaCacheKey, out var count))
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                count = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM api_usage_logs WHERE provider = 'google-places'");
                _cache.Set(QuotaCacheKey, count, QuotaCacheTtl);
            }
            catch (Exception ex)
            {
                // If we can't read the count, fail safe: block the call.
                _logger.LogError(ex, "GooglePlacesService: quota check DB query failed — blocking request");
                return true;
            }
        }

        if (count < max) return false;

        _logger.LogCritical(
            "GooglePlacesService: hard quota of {Max} requests reached (logged: {Count}). " +
            "All Google Places API calls are disabled. " +
            "Raise Integration__GooglePlacesMaxRequests or set it to 0 to disable the cap.",
            max, count);
        return true;
    }
}
