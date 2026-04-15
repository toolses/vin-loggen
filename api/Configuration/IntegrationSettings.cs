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

    /// <summary>
    /// Hard daily ceiling on Gemini API requests (across all endpoints).
    /// Once reached, Gemini calls return error/fallback results.
    /// Set to 0 to disable the cap (unlimited). Default: 500.
    /// Override via <c>Integration__GeminiMaxDailyRequests</c>.
    /// </summary>
    public int GeminiMaxDailyRequests { get; init; } = 500;

    /// <summary>
    /// Hard daily ceiling on WineAPI requests (across all endpoints).
    /// Once reached, WineAPI calls return null.
    /// Set to 0 to disable the cap (unlimited). Default: 100.
    /// Override via <c>Integration__WineApiMaxDailyRequests</c>.
    /// </summary>
    public int WineApiMaxDailyRequests { get; init; } = 100;

    /// <summary>Enable Vinmonopolet product catalogue lookups (not yet implemented).</summary>
    public bool EnableVinmonopolet { get; init; } = false;

    /// <summary>
    /// Maximum Pro-feature scans per day for free-tier users.
    /// Set to 10 during the testing period.
    /// </summary>
    public int DailyProLimit { get; init; } = 10;

    public WineApiSettings WineApi { get; init; } = new();

    public DeepSeekSettings DeepSeek { get; init; } = new();

    public GroqSettings Groq { get; init; } = new();

    public AiFallbackSettings AiFallback { get; init; } = new();

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
    public string BaseUrl { get; init; } = "https://api.wineapi.io";
    public string AuthHeader { get; init; } = "X-API-Key";
    public string AuthPrefix { get; init; } = "";
}

/// <summary>Connection details for the DeepSeek API (OpenAI-compatible).</summary>
public sealed class DeepSeekSettings
{
    public string BaseUrl { get; init; } = "https://api.deepseek.com";
}

/// <summary>Connection details for the Groq API (OpenAI-compatible).</summary>
public sealed class GroqSettings
{
    public string BaseUrl { get; init; } = "https://api.groq.com/openai";
}

/// <summary>
/// Configures which AI providers are tried (in order) for each capability.
/// Each entry is a provider name: "Groq", "DeepSeek", "Gemini".
/// </summary>
public sealed class AiFallbackSettings
{
    /// <summary>Provider priority for label scanning (vision). Default: Groq (Llama 4 Scout), then Gemini fallback.</summary>
    public string[] LabelScanPriority { get; init; } = ["Groq", "Gemini"];

    /// <summary>Provider priority for expert chat. Default: Groq (Qwen 3), DeepSeek, then Gemini fallback.</summary>
    public string[] ExpertChatPriority { get; init; } = ["Groq", "DeepSeek", "Gemini"];

    /// <summary>Provider priority for wine enrichment (food pairings, descriptions). Default: DeepSeek, then Groq fallback.</summary>
    public string[] EnrichmentPriority { get; init; } = ["DeepSeek", "Groq", "Gemini"];

    /// <summary>
    /// Groq tokens-per-minute budget. When recent Groq usage within a 60-second
    /// sliding window exceeds this threshold, the provider chain skips Groq and
    /// falls back to the next provider. Default: 5500 (Groq Qwen 3 limit is 6000 TPM).
    /// </summary>
    public int GroqTokenBudgetPerMinute { get; init; } = 5500;
}
