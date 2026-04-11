using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Services;

// ── Public response DTOs ──────────────────────────────────────────────────────

public record WineAnalysisResponse(
    // ── Basic (OCR) – always populated ───────────────────────────────────────
    string?   WineName,
    string?   Producer,
    int?      Vintage,
    string?   Country,
    string?   Region,
    string[]? Grapes,
    string?   Type,
    double?   AlcoholContent,
    // ── Deduplication – set by orchestrator after DB look-up ─────────────────
    bool      AlreadyTasted   = false,
    Guid?     ExistingWineId  = null,
    decimal?  LastRating      = null,
    DateOnly? LastTastedAt    = null,
    // ── Pro enrichment – set by orchestrator when quota is available ──────────
    string[]? FoodPairings    = null,   // from wineapi.io or Gemini Pro prompt
    string?   Description     = null,   // from wineapi.io
    string?   TechnicalNotes  = null,   // from wineapi.io
    string?   ExternalSourceId = null,  // wineapi.io catalogue ID
    // ── Quota metadata – always returned for UI ───────────────────────────────
    bool      ProLimitReached = false,
    int       ProScansToday   = 0,
    int       DailyProLimit   = 10,
    bool      IsPro           = false
);

/// <summary>
/// Minimal wine info used as input for taste-profile generation.
/// Combines master data from <c>wines</c> with the user's rating from <c>wine_logs</c>.
/// </summary>
public record WineProfileData(
    string   Name,
    string   Producer,
    int?     Vintage,
    string   Type,
    string?  Country,
    string?  Region,
    decimal? Rating
);

public record TasteProfileResponse(
    string[]  PreferredTypes,
    string[]  TopCountries,
    string[]  TopRegions,
    decimal   AverageRating,
    string?   VintageRange,
    string[]  FlavorDescriptors,
    string    Recommendation,
    string    PersonalityTitle
);

// ── Result wrapper ────────────────────────────────────────────────────────────

public record GeminiResult<T>(T? Value, string? Error)
{
    public bool IsSuccess => Value is not null;
}

// ── Service ───────────────────────────────────────────────────────────────────

public sealed class GeminiService : IGeminiService
{
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent";

    // Norwegian prompt as specified in the requirements
    private const string SystemPrompt = """
        Du er en sommelier-ekspert. Analyser bildet av vinetiketten og returner KUN rå JSON (uten markdown-formatering).
        JSON-struktur:
        {
          "wineName": string,
          "producer": string,
          "vintage": integer,
          "country": string,
          "region": string,
          "grapes": string[],
          "type": "Rød"|"Hvit"|"Musserende"|"Rosé"|"Oransje",
          "alcoholContent": number
        }
        Hvis du er usikker på et felt, sett det til null.
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration     _configuration;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(
        IHttpClientFactory      httpClientFactory,
        IConfiguration          configuration,
        ILogger<GeminiService>  logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration     = configuration;
        _logger            = logger;
    }

    public async Task<GeminiResult<WineAnalysisResponse>> AnalyzeLabelAsync(
        byte[]            imageBytes,
        string            mimeType,
        CancellationToken ct)
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("GeminiService: GEMINI_API_KEY is not configured");
            return new(null, "GEMINI_API_KEY is not configured");
        }

        var base64Image = Convert.ToBase64String(imageBytes);

        // Build request using anonymous types – keeps the file-scoped record list short
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = SystemPrompt },
                        new { inline_data = new { mime_type = mimeType, data = base64Image } },
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient("gemini");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync($"{GeminiEndpoint}?key={apiKey}", payload, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "GeminiService: HTTP request to Gemini timed out");
            return new(null, "Gemini API request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiService: HTTP request to Gemini failed");
            return new(null, $"HTTP request to Gemini failed: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GeminiService: Gemini returned {Status}: {Body}", response.StatusCode, errorBody);
            return new(null, $"Gemini returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }

        // Extract the text field from the Gemini response structure
        string rawJson;
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            rawJson = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiService: failed to navigate Gemini response structure");
            return new(null, "Failed to parse Gemini response structure");
        }

        // Strip markdown code fences if Gemini includes them despite instructions
        rawJson = Regex.Replace(
            rawJson.Trim(),
            @"^```(?:json)?\s*|\s*```$",
            "",
            RegexOptions.Multiline).Trim();

        try
        {
            var extraction = JsonSerializer.Deserialize<WineAnalysisResponse>(
                rawJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "GeminiService: extracted '{WineName}' ({Vintage})", extraction?.WineName, extraction?.Vintage);

            return new(extraction, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "GeminiService: JSON parsing failed. Raw: {Raw}", rawJson);
            return new(null, $"Failed to parse AI response as wine data");
        }
    }

    // ── Pro: food-pairing enrichment ─────────────────────────────────────────

    private const string FoodPairingPromptTemplate =
        """
        Du er en sommelier-ekspert. Basert på vinen nedenfor, generer matanbefalinger og tekniske smaksnotater.
        Returner KUN rå JSON (uten markdown-formatering).
        JSON-struktur:
        {{
          "foodPairings": string[],
          "technicalNotes": string
        }}
        - foodPairings: 3-5 konkrete matanbefalinger på norsk (f.eks. "Lammekoteletter", "Modnet parmesan")
        - technicalNotes: 1-2 setninger med tekniske smaksnotater på norsk (tanniner, syre, finish)

        Vin: {0} {1}, {2}, {3} {4}
        """;

    public record FoodPairingResult(string[] FoodPairings, string? TechnicalNotes);

    /// <summary>
    /// Pro-tier call: ask Gemini for food pairings and technical tasting notes
    /// based on the wine's known characteristics. Used as a fallback when
    /// wineapi.io returns no food-pairing data.
    /// </summary>
    public async Task<FoodPairingResult?> GetFoodPairingsAsync(
        string? wineName,
        string? producer,
        int?    vintage,
        string? type,
        string? country,
        CancellationToken ct)
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var prompt = string.Format(
            FoodPairingPromptTemplate,
            producer ?? "",
            wineName ?? "",
            vintage?.ToString() ?? "ukjent årgang",
            type ?? "",
            country ?? "");

        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } }
        };

        var client = _httpClientFactory.CreateClient("gemini");
        try
        {
            var response = await client.PostAsJsonAsync($"{GeminiEndpoint}?key={apiKey}", payload, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var rawJson = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            rawJson = Regex.Replace(rawJson.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

            var result = JsonSerializer.Deserialize<FoodPairingResult>(
                rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "GeminiService.GetFoodPairings: {Count} pairings for '{Wine}'",
                result?.FoodPairings?.Length, wineName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeminiService.GetFoodPairings: failed for '{Wine}'", wineName);
            return null;
        }
    }

    private const string TasteProfilePrompt = """
        Du er en sommelier-ekspert. Analyser brukerens vinsamling og lag en personlig smaksprofil.
        Returner KUN rå JSON (uten markdown-formatering).
        JSON-struktur:
        {
          "preferredTypes": string[],
          "topCountries": string[],
          "topRegions": string[],
          "averageRating": number,
          "vintageRange": string,
          "flavorDescriptors": string[],
          "recommendation": string,
          "personalityTitle": string
        }
        - preferredTypes: De vindtypene brukeren foretrekker mest (f.eks. "Rød", "Hvit")
        - topCountries: Landene brukeren kjøper mest vin fra
        - topRegions: Regionene brukeren foretrekker
        - averageRating: Gjennomsnittlig rating
        - vintageRange: Årgang-spenn (f.eks. "2018-2022")
        - flavorDescriptors: 4-6 smaksbeskrivelser som passer brukerens profil (f.eks. "Bærfrukt", "Krydder", "Eik")
        - recommendation: 2-3 setninger med personlig anbefaling på norsk, basert på samlingen
        - personalityTitle: Et kreativt norsk kallenavn for vinpersonligheten (f.eks. "Burgundy-elskeren", "Verdensreisende", "Tradisjonalisten")
        """;

    public async Task<TasteProfileResponse?> GenerateTasteProfileAsync(
        IEnumerable<WineProfileData> wines,
        CancellationToken ct)
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("GeminiService: GEMINI_API_KEY is not configured");
            return null;
        }

        // Build a text summary of the wine collection
        var wineList = wines.Select(w => new
        {
            w.Name,
            w.Producer,
            w.Vintage,
            w.Type,
            w.Country,
            w.Region,
            w.Rating,
        });

        var collectionJson = JsonSerializer.Serialize(wineList, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        });

        var userPrompt = $"Her er brukerens vinsamling ({wines.Count()} viner):\n{collectionJson}";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = TasteProfilePrompt },
                        new { text = userPrompt },
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient("gemini");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync($"{GeminiEndpoint}?key={apiKey}", payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiService: HTTP request to Gemini failed (taste profile)");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GeminiService: Gemini returned {Status} for taste profile: {Body}",
                response.StatusCode, errorBody);
            return null;
        }

        string rawJson;
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            rawJson = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiService: failed to navigate Gemini response structure (taste profile)");
            return null;
        }

        rawJson = Regex.Replace(
            rawJson.Trim(),
            @"^```(?:json)?\s*|\s*```$",
            "",
            RegexOptions.Multiline).Trim();

        try
        {
            var profile = JsonSerializer.Deserialize<TasteProfileResponse>(
                rawJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "GeminiService: generated taste profile '{Title}'", profile?.PersonalityTitle);

            return profile;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "GeminiService: taste profile JSON parsing failed. Raw: {Raw}", rawJson);
            return null;
        }
    }
}
