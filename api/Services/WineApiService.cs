using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Services;

/// <summary>
/// REST client for the wineapi.io wine catalogue API.
///
/// Authentication: reads <c>WINE_API_KEY</c> from environment / configuration.
/// The API key must NOT be committed to source – add it to your Render / Vercel
/// environment variables as <c>WINE_API_KEY</c>.
///
/// NOTE: The exact endpoint paths and response schema are based on common wine-API
/// conventions. Verify against the official wineapi.io documentation and update
/// <see cref="SearchPath"/> / <see cref="WineApiHit"/> if the schema differs.
///
/// HTTP resilience (retry + circuit breaker) is configured in Program.cs via the
/// "wineApi" named <see cref="IHttpClientFactory"/> registration.
/// </summary>
public sealed class WineApiService : IWineApiService
{
    // ── Configurable constants ────────────────────────────────────────────────

    /// <summary>Path appended to <see cref="IntegrationSettings.WineApiSettings.BaseUrl"/>.</summary>
    private const string SearchPath = "/v1/wines/search";

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public sealed record WineEnrichment(
        string?   ExternalId,
        string?   Description,
        string[]? FoodPairings,
        string?   TechnicalNotes,
        double?   AlcoholContent,
        string[]? Grapes,
        string?   SuggestedName     = null,
        string?   SuggestedProducer = null
    );

    // Internal deserialization types (mirrors expected wineapi.io response)
    private sealed record SearchResponse(
        [property: JsonPropertyName("wines")] List<WineApiHit>? Wines
    );

    internal sealed record WineApiHit(
        [property: JsonPropertyName("id")]          string?   Id,
        [property: JsonPropertyName("name")]        string?   Name,
        [property: JsonPropertyName("producer")]    string?   Producer,
        [property: JsonPropertyName("vintage")]     int?      Vintage,
        [property: JsonPropertyName("description")] string?   Description,
        [property: JsonPropertyName("food_pairing")] string[]? FoodPairing,
        [property: JsonPropertyName("food_pairings")] string[]? FoodPairings,
        [property: JsonPropertyName("technical_notes")] string? TechnicalNotes,
        [property: JsonPropertyName("alcohol_content")] double? AlcoholContent,
        [property: JsonPropertyName("grapes")]      string[]? Grapes
    );

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration     _configuration;
    private readonly IntegrationSettings _settings;
    private readonly ILogger<WineApiService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IApiUsageService _apiUsage;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public WineApiService(
        IHttpClientFactory       httpClientFactory,
        IConfiguration           configuration,
        IntegrationSettings      settings,
        ILogger<WineApiService>  logger,
        IMemoryCache             cache,
        IApiUsageService         apiUsage)
    {
        _httpClientFactory = httpClientFactory;
        _configuration     = configuration;
        _settings          = settings;
        _logger            = logger;
        _cache             = cache;
        _apiUsage          = apiUsage;
    }

    /// <summary>
    /// Searches for a wine in the wineapi.io catalogue and returns enrichment
    /// data for the best match. Returns <c>null</c> when the feature is disabled,
    /// no API key is configured, no match is found, or the API call fails.
    /// </summary>
    public async Task<WineEnrichment?> FindAsync(
        string  producer,
        string  name,
        int?    vintage,
        CancellationToken ct)
    {
        if (!_settings.EnableWineApi)
        {
            _logger.LogDebug("WineApiService: disabled by configuration, skipping");
            return null;
        }

        var apiKey = _configuration["WINE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("WineApiService: WINE_API_KEY not configured, skipping");
            return null;
        }

        // ── Cache lookup ────────────────────────────────────────────────────────
        var cacheKey = $"wineapi:{producer.Trim().ToLowerInvariant()}|{name.Trim().ToLowerInvariant()}|{vintage?.ToString() ?? "nv"}";
        if (_cache.TryGetValue<WineEnrichment?>(cacheKey, out var cached))
        {
            _logger.LogDebug("WineApiService: cache hit for '{CacheKey}'", cacheKey);
            return cached;
        }

        // Build query string: free-text search combining key identifiers
        var query = vintage.HasValue
            ? $"{producer} {name} {vintage}"
            : $"{producer} {name}";

        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}{SearchPath}?q={Uri.EscapeDataString(query)}&limit=5";

        _logger.LogInformation("WineApiService: searching for '{Query}'", query);

        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");

            // Set auth header per-request so the client remains stateless
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(
                cfg.AuthHeader,
                $"{cfg.AuthPrefix}{apiKey}");

            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            _ = _apiUsage.LogAsync("wineapi", SearchPath, (int)response.StatusCode, (int)sw.ElapsedMilliseconds, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "WineApiService: HTTP {Status} from wineapi.io", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SearchResponse>(body, JsonOpts);

            var hit = FindBestMatch(result?.Wines, producer, name, vintage);
            if (hit is null)
            {
                _logger.LogInformation("WineApiService: no match for '{Query}'", query);
                _cache.Set(cacheKey, (WineEnrichment?)null, CacheTtl);
                return null;
            }

            _logger.LogInformation(
                "WineApiService: matched '{Name}' (id={Id})", hit.Name, hit.Id);

            // Normalise the two possible food-pairing field names
            var foodPairings = (hit.FoodPairings ?? hit.FoodPairing)
                              ?.Where(s => !string.IsNullOrWhiteSpace(s))
                              .ToArray();

            var enrichment = new WineEnrichment(
                ExternalId:    hit.Id,
                Description:   hit.Description,
                FoodPairings:  foodPairings?.Length > 0 ? foodPairings : null,
                TechnicalNotes: hit.TechnicalNotes,
                AlcoholContent: hit.AlcoholContent,
                Grapes:        hit.Grapes,
                SuggestedName:     hit.Name,
                SuggestedProducer: hit.Producer
            );

            _cache.Set(cacheKey, (WineEnrichment?)enrichment, CacheTtl);
            return enrichment;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WineApiService: request failed for '{Query}'", query);
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Picks the best hit from the result list using a simple scoring heuristic:
    /// exact producer + name match beats partial match; vintage match is a bonus.
    /// </summary>
    internal static WineApiHit? FindBestMatch(
        List<WineApiHit>? hits,
        string producer, string name, int? vintage)
    {
        if (hits is null || hits.Count == 0) return null;

        var lp = producer.Trim().ToLowerInvariant();
        var ln = name.Trim().ToLowerInvariant();

        return hits
            .Select(h => new
            {
                Hit   = h,
                Score = Score(h, lp, ln, vintage),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault()?.Hit;
    }

    internal static int Score(WineApiHit h, string lp, string ln, int? vintage)
    {
        int score = 0;
        var hp = (h.Producer ?? "").Trim().ToLowerInvariant();
        var hn = (h.Name     ?? "").Trim().ToLowerInvariant();

        if (hn.Contains(ln) || ln.Contains(hn)) score += 2;
        if (hp.Contains(lp) || lp.Contains(hp)) score += 2;
        if (vintage.HasValue && h.Vintage == vintage)            score += 1;

        return score;
    }
}
