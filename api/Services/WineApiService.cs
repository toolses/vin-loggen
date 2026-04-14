using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
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
    private const string SearchPath = "/wines/search";

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

    /// <summary>Rich wine identification result from /identify/text.</summary>
    public sealed record WineIdentification(
        string    Id,
        string    Name,
        string?   Winery,
        int?      Vintage,
        string?   Type,
        string?   Region,
        string?   Country,
        string?   Description,
        string[]? FoodPairings,
        string?   TechnicalNotes,
        double?   AlcoholContent,
        double?   AverageRating,
        int?      RatingsCount
    );

    // Internal deserialization types (mirrors actual wineapi.io response)
    private sealed record SearchResponse(
        [property: JsonPropertyName("results")] List<WineApiHit>? Results
    );

    internal sealed record WineApiHit(
        [property: JsonPropertyName("id")]              string?   Id,
        [property: JsonPropertyName("name")]            string?   Name,
        [property: JsonPropertyName("winery")]          string?   Winery,
        [property: JsonPropertyName("vintage")]         int?      Vintage,
        [property: JsonPropertyName("type")]            string?   Type,
        [property: JsonPropertyName("region")]          string?   Region,
        [property: JsonPropertyName("country")]         string?   Country,
        [property: JsonPropertyName("averageRating")]   double?   AverageRating,
        [property: JsonPropertyName("ratingsCount")]    int?      RatingsCount,
        [property: JsonPropertyName("confidence")]      double?   Confidence
    );

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration     _configuration;
    private readonly IntegrationSettings _settings;
    private readonly ILogger<WineApiService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IApiUsageService _apiUsage;
    private readonly NpgsqlDataSource _dataSource;

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
        IApiUsageService         apiUsage,
        NpgsqlDataSource         dataSource)
    {
        _httpClientFactory = httpClientFactory;
        _configuration     = configuration;
        _settings          = settings;
        _logger            = logger;
        _cache             = cache;
        _apiUsage          = apiUsage;
        _dataSource        = dataSource;
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
        CancellationToken ct,
        Guid?   userId        = null,
        Guid?   correlationId = null)
    {
        if (!_settings.EnableWineApi)
        {
            _logger.LogDebug("WineApiService: disabled by configuration, skipping");
            return null;
        }

        if (await ApiQuotaGuard.IsDailyQuotaExceededAsync("wineapi", _settings.WineApiMaxDailyRequests, _cache, _dataSource, _logger, ct))
            return null;

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

        // Build free-text query: "producer name vintage"
        var query = $"{producer.Trim()} {name.Trim()}{(vintage.HasValue ? $" {vintage.Value}" : "")}".Trim();
        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}{SearchPath}?q={Uri.EscapeDataString(query)}&limit=15";

        _logger.LogInformation("WineApiService: searching for q='{Query}'", query);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");

            // Set auth header per-request so the client remains stateless
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(
                cfg.AuthHeader,
                $"{cfg.AuthPrefix}{apiKey}");

            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("wineapi", SearchPath, (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: url, responseBody: body, correlationId: correlationId);
            ApiQuotaGuard.EvictCache("wineapi", _cache);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "WineApiService: HTTP {Status} from wineapi.io", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<SearchResponse>(body, JsonOpts);

            var hit = FindBestMatch(result?.Results, producer, name, vintage);
            if (hit is null)
            {
                _logger.LogInformation("WineApiService: no match for q='{Query}'", query);
                _cache.Set(cacheKey, (WineEnrichment?)null, CacheTtl);
                return null;
            }

            _logger.LogInformation(
                "WineApiService: matched '{Name}' (id={Id})", hit.Name, hit.Id);

            // Search endpoint returns identification data only — no description/pairings/notes.
            // Those will be filled by the enrichment fallback (AI) if needed.
            var enrichment = new WineEnrichment(
                ExternalId:    hit.Id,
                Description:   null,
                FoodPairings:  null,
                TechnicalNotes: null,
                AlcoholContent: null,
                Grapes:        null,
                SuggestedName:     hit.Name,
                SuggestedProducer: hit.Winery
            );

            _cache.Set(cacheKey, (WineEnrichment?)enrichment, CacheTtl);
            return enrichment;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "WineApiService: request failed for q='{Query}'", query);
            _ = _apiUsage.LogAsync("wineapi", SearchPath, null, (int)sw.ElapsedMilliseconds,
                userId, CancellationToken.None, requestBody: url, responseBody: ex.Message, correlationId: correlationId);
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
        var hp = (h.Winery ?? "").Trim().ToLowerInvariant();
        var hn = (h.Name     ?? "").Trim().ToLowerInvariant();

        if (hn.Contains(ln) || ln.Contains(hn)) score += 2;
        if (hp.Contains(lp) || lp.Contains(hp)) score += 2;
        if (score > 0 && vintage.HasValue && h.Vintage == vintage) score += 1;

        return score;
    }

    // ── Text-based identification ────────────────────────────────────────────

    /// <summary>Wrapper for POST /identify/text (actual wineapi.io response).</summary>
    private sealed record IdentifyTextResponse(
        [property: JsonPropertyName("wine")]       IdentifyWineHit? Wine,
        [property: JsonPropertyName("suggestions")] List<IdentifyWineHit>? Suggestions,
        [property: JsonPropertyName("confidence")]  double? Confidence
    );

    private sealed record IdentifyWineHit(
        [property: JsonPropertyName("id")]            string?  Id,
        [property: JsonPropertyName("name")]          string?  Name,
        [property: JsonPropertyName("vintage")]       int?     Vintage,
        [property: JsonPropertyName("type")]          string?  Type,
        [property: JsonPropertyName("region")]        string?  Region,
        [property: JsonPropertyName("country")]       string?  Country,
        [property: JsonPropertyName("averageRating")] double?  AverageRating,
        [property: JsonPropertyName("ratingsCount")]  int?     RatingsCount
    );

    /// <inheritdoc />
    public async Task<WineIdentification?> IdentifyByTextAsync(
        string query, CancellationToken ct,
        Guid?  userId        = null,
        Guid?  correlationId = null)
    {
        if (!_settings.EnableWineApi)
        {
            _logger.LogDebug("WineApiService.IdentifyByText: disabled by configuration");
            return null;
        }

        if (await ApiQuotaGuard.IsDailyQuotaExceededAsync("wineapi", _settings.WineApiMaxDailyRequests, _cache, _dataSource, _logger, ct))
            return null;

        var apiKey = _configuration["WINE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("WineApiService.IdentifyByText: WINE_API_KEY not configured");
            return null;
        }

        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}/identify/text";

        _logger.LogInformation("WineApiService.IdentifyByText: query='{Query}'", query);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { query })
            };
            request.Headers.TryAddWithoutValidation(cfg.AuthHeader, $"{cfg.AuthPrefix}{apiKey}");

            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("wineapi", "/identify/text", (int)response.StatusCode,
                (int)sw.ElapsedMilliseconds, userId, ct,
                requestBody: $"{{\"query\":\"{query}\"}}",
                responseBody: body, correlationId: correlationId);
            ApiQuotaGuard.EvictCache("wineapi", _cache);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WineApiService.IdentifyByText: HTTP {Status}", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<IdentifyTextResponse>(body, JsonOpts);
            var wine = result?.Wine;

            if (wine?.Id is null || wine.Name is null)
            {
                _logger.LogInformation("WineApiService.IdentifyByText: no match for '{Query}'", query);
                return null;
            }

            _logger.LogInformation(
                "WineApiService.IdentifyByText: matched '{Name}' (id={Id}, rating={Rating})",
                wine.Name, wine.Id, wine.AverageRating);

            return new WineIdentification(
                Id:              wine.Id,
                Name:            wine.Name,
                Winery:          null,       // search/identify endpoints don't return winery
                Vintage:         wine.Vintage,
                Type:            wine.Type,
                Region:          wine.Region,
                Country:         wine.Country,
                Description:     null,       // not returned by identify/text
                FoodPairings:    null,        // not returned by identify/text
                TechnicalNotes:  null,        // not returned by identify/text
                AlcoholContent:  null,        // not returned by identify/text
                AverageRating:   wine.AverageRating,
                RatingsCount:    wine.RatingsCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "WineApiService.IdentifyByText: request failed for '{Query}'", query);
            _ = _apiUsage.LogAsync("wineapi", "/identify/text", null, (int)sw.ElapsedMilliseconds,
                userId, CancellationToken.None, requestBody: $"{{\"query\":\"{query}\"}}",
                responseBody: ex.Message, correlationId: correlationId);
            return null;
        }
    }

    // ── Admin test: raw search results ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<WineApiSearchHitDto>?> SearchRawAsync(
        string producer, string name, int? vintage,
        CancellationToken ct,
        Guid? userId = null, Guid? correlationId = null)
    {
        if (!_settings.EnableWineApi)
            return null;

        var apiKey = _configuration["WINE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var query = $"{producer.Trim()} {name.Trim()}{(vintage.HasValue ? $" {vintage.Value}" : "")}".Trim();
        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}{SearchPath}?q={Uri.EscapeDataString(query)}&limit=15";

        _logger.LogInformation("WineApiService.SearchRaw: q='{Query}'", query);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(cfg.AuthHeader, $"{cfg.AuthPrefix}{apiKey}");

            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("wineapi", SearchPath, (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: url, responseBody: body, correlationId: correlationId);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WineApiService.SearchRaw: HTTP {Status}", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<SearchResponse>(body, JsonOpts);
            return result?.Results?.Select(h => new WineApiSearchHitDto(
                h.Id, h.Name, h.Winery, h.Vintage, h.Type, h.Region, h.Country,
                h.AverageRating, h.RatingsCount, h.Confidence)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "WineApiService.SearchRaw: request failed for q='{Query}'", query);
            _ = _apiUsage.LogAsync("wineapi", SearchPath, null, (int)sw.ElapsedMilliseconds,
                userId, CancellationToken.None, requestBody: url, responseBody: ex.Message, correlationId: correlationId);
            return null;
        }
    }

    // ── Admin test: raw details by ID ────────────────────────────────────────

    /// <inheritdoc />
    public async Task<WineApiDetailDto?> GetDetailsRawAsync(
        string wineId, CancellationToken ct,
        Guid? userId = null, Guid? correlationId = null)
    {
        if (!_settings.EnableWineApi)
            return null;

        var apiKey = _configuration["WINE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}/wines/{Uri.EscapeDataString(wineId)}";

        _logger.LogInformation("WineApiService.GetDetailsRaw: wineId={WineId}", wineId);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(cfg.AuthHeader, $"{cfg.AuthPrefix}{apiKey}");

            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("wineapi", $"/wines/{wineId}", (int)response.StatusCode,
                (int)sw.ElapsedMilliseconds, userId, ct,
                requestBody: url, responseBody: body, correlationId: correlationId);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WineApiService.GetDetailsRaw: HTTP {Status} for {WineId}", response.StatusCode, wineId);
                return null;
            }

            var d = JsonSerializer.Deserialize<WineDetailsResponse>(body, JsonOpts);
            if (d is null) return null;

            return new WineApiDetailDto(
                d.Id, d.Name, d.Winery, d.Vintage, d.Type, d.Region, d.Country,
                d.Description, d.FoodPairings, d.TechnicalNotes, d.AlcoholContent,
                d.Grapes, d.AverageRating, d.RatingsCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "WineApiService.GetDetailsRaw: failed for {WineId}", wineId);
            _ = _apiUsage.LogAsync("wineapi", $"/wines/{wineId}", null, (int)sw.ElapsedMilliseconds,
                userId, CancellationToken.None, requestBody: url, responseBody: ex.Message, correlationId: correlationId);
            return null;
        }
    }

    // ── Admin test: raw identify by text ─────────────────────────────────────

    /// <inheritdoc />
    public async Task<WineApiIdentifyResultDto?> IdentifyByTextRawAsync(
        string query, CancellationToken ct,
        Guid? userId = null, Guid? correlationId = null)
    {
        if (!_settings.EnableWineApi)
            return null;

        var apiKey = _configuration["WINE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}/identify/text";

        _logger.LogInformation("WineApiService.IdentifyByTextRaw: query='{Query}'", query);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { query })
            };
            request.Headers.TryAddWithoutValidation(cfg.AuthHeader, $"{cfg.AuthPrefix}{apiKey}");

            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("wineapi", "/identify/text", (int)response.StatusCode,
                (int)sw.ElapsedMilliseconds, userId, ct,
                requestBody: $"{{\"query\":\"{query}\"}}",
                responseBody: body, correlationId: correlationId);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WineApiService.IdentifyByTextRaw: HTTP {Status}", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<IdentifyTextResponse>(body, JsonOpts);
            if (result is null) return null;

            return new WineApiIdentifyResultDto(
                Wine: result.Wine is { } w
                    ? new WineApiIdentifyHitDto(w.Id, w.Name, w.Vintage, w.Type, w.Region, w.Country, w.AverageRating, w.RatingsCount)
                    : null,
                Suggestions: result.Suggestions?.Select(s =>
                    new WineApiIdentifyHitDto(s.Id, s.Name, s.Vintage, s.Type, s.Region, s.Country, s.AverageRating, s.RatingsCount)).ToList(),
                Confidence: result.Confidence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "WineApiService.IdentifyByTextRaw: failed for '{Query}'", query);
            _ = _apiUsage.LogAsync("wineapi", "/identify/text", null, (int)sw.ElapsedMilliseconds,
                userId, CancellationToken.None, requestBody: $"{{\"query\":\"{query}\"}}",
                responseBody: ex.Message, correlationId: correlationId);
            return null;
        }
    }

    // ── Wine details by ID ─────────────────────────────────────────────────

    /// <summary>Deserialization DTO for GET /wines/{id}.</summary>
    private sealed record WineDetailsResponse(
        [property: JsonPropertyName("id")]              string?   Id,
        [property: JsonPropertyName("name")]            string?   Name,
        [property: JsonPropertyName("winery")]          string?   Winery,
        [property: JsonPropertyName("vintage")]         int?      Vintage,
        [property: JsonPropertyName("type")]            string?   Type,
        [property: JsonPropertyName("region")]          string?   Region,
        [property: JsonPropertyName("country")]         string?   Country,
        [property: JsonPropertyName("description")]     string?   Description,
        [property: JsonPropertyName("foodPairings")]    string[]? FoodPairings,
        [property: JsonPropertyName("technicalNotes")]  string?   TechnicalNotes,
        [property: JsonPropertyName("alcoholContent")]  double?   AlcoholContent,
        [property: JsonPropertyName("grapes")]          string[]? Grapes,
        [property: JsonPropertyName("averageRating")]   double?   AverageRating,
        [property: JsonPropertyName("ratingsCount")]    int?      RatingsCount
    );

    /// <inheritdoc />
    public async Task<WineEnrichment?> GetDetailsAsync(string wineId, CancellationToken ct,
        Guid?  userId        = null,
        Guid?  correlationId = null)
    {
        if (!_settings.EnableWineApi)
        {
            _logger.LogDebug("WineApiService.GetDetails: disabled by configuration");
            return null;
        }

        if (await ApiQuotaGuard.IsDailyQuotaExceededAsync("wineapi", _settings.WineApiMaxDailyRequests, _cache, _dataSource, _logger, ct))
            return null;

        var apiKey = _configuration["WINE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("WineApiService.GetDetails: WINE_API_KEY not configured");
            return null;
        }

        var cfg = _settings.WineApi;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}/wines/{Uri.EscapeDataString(wineId)}";

        _logger.LogInformation("WineApiService.GetDetails: fetching details for wine id={WineId}", wineId);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("wineApi");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(cfg.AuthHeader, $"{cfg.AuthPrefix}{apiKey}");

            using var response = await client.SendAsync(request, ct);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("wineapi", $"/wines/{wineId}", (int)response.StatusCode,
                (int)sw.ElapsedMilliseconds, userId, ct,
                requestBody: url, responseBody: body, correlationId: correlationId);
            ApiQuotaGuard.EvictCache("wineapi", _cache);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WineApiService.GetDetails: HTTP {Status} for wine {WineId}", response.StatusCode, wineId);
                return null;
            }

            var detail = JsonSerializer.Deserialize<WineDetailsResponse>(body, JsonOpts);

            if (detail is null)
            {
                _logger.LogInformation("WineApiService.GetDetails: empty response for wine {WineId}", wineId);
                return null;
            }

            _logger.LogInformation(
                "WineApiService.GetDetails: got details for '{Name}' (alcohol={Alcohol}, grapes={GrapeCount})",
                detail.Name, detail.AlcoholContent, detail.Grapes?.Length);

            return new WineEnrichment(
                ExternalId:     detail.Id,
                Description:    detail.Description,
                FoodPairings:   detail.FoodPairings,
                TechnicalNotes: detail.TechnicalNotes,
                AlcoholContent: detail.AlcoholContent,
                Grapes:         detail.Grapes,
                SuggestedName:     detail.Name,
                SuggestedProducer: detail.Winery);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "WineApiService.GetDetails: request failed for wine {WineId}", wineId);
            _ = _apiUsage.LogAsync("wineapi", $"/wines/{wineId}", null, (int)sw.ElapsedMilliseconds,
                userId, CancellationToken.None, requestBody: url, responseBody: ex.Message, correlationId: correlationId);
            return null;
        }
    }
}
