namespace VinLoggen.Api.Configuration;

/// <summary>
/// Bound from the <c>Integration</c> section of appsettings.json / env vars.
/// All toggles default to the conservative value so a misconfigured deployment
/// degrades gracefully rather than incurring unexpected API costs.
/// </summary>
public sealed class IntegrationSettings
{
    public const string SectionName = "Integration";

    /// <summary>Enable Gemini AI label analysis. Disable to return stub data only.</summary>
    public bool EnableGemini { get; init; } = true;

    /// <summary>Enable wineapi.io enrichment calls (Pro tier step).</summary>
    public bool EnableWineApi { get; init; } = true;

    /// <summary>Enable Vinmonopolet product catalogue lookups (not yet implemented).</summary>
    public bool EnableVinmonopolet { get; init; } = false;

    /// <summary>
    /// Maximum Pro-feature scans per day for free-tier users.
    /// Set to 10 during the testing period.
    /// </summary>
    public int DailyProLimit { get; init; } = 10;

    public WineApiSettings WineApi { get; init; } = new();

    /// <summary>
    /// Hard ceiling on the total number of requests sent to the Google Places API.
    /// Once this count (measured against <c>api_usage_logs</c>) is reached ALL
    /// Google Places calls are refused and empty results are returned.
    /// Set to 0 to disable the cap (unlimited).  Default: 10 000.
    /// Override via the <c>Integration__GooglePlacesMaxRequests</c> environment variable.
    /// </summary>
    public int GooglePlacesMaxRequests { get; init; } = 10_000;
}

/// <summary>Connection details for the wineapi.io REST API.</summary>
public sealed class WineApiSettings
{
    /// <summary>
    /// Base URL for the wineapi.io REST API.
    /// Adjust if the actual base URL differs from this default.
    /// The API key is read from the <c>WINE_API_KEY</c> environment variable.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.wineapi.io";

    /// <summary>
    /// Name of the HTTP header used for authentication.
    /// wineapi.io uses <c>X-API-Key</c> with a raw key value (no prefix).
    /// </summary>
    public string AuthHeader { get; init; } = "X-API-Key";

    /// <summary>
    /// Prefix added before the key value in the auth header.
    /// Empty for raw API-key headers; use "Bearer " for OAuth-style.
    /// </summary>
    public string AuthPrefix { get; init; } = "";
}
