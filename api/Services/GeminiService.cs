using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Json;

namespace VinLoggen.Api.Services;

// ── Public response DTO (used by both this service and the endpoint layer) ────

public record WineAnalysisResponse(
    string?   WineName,
    string?   Producer,
    int?      Vintage,
    string?   Country,
    string?   Region,
    string[]? Grapes,
    string?   Type,
    double?   AlcoholContent
);

// ── Service ───────────────────────────────────────────────────────────────────

public sealed class GeminiService
{
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

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

    public async Task<WineAnalysisResponse?> AnalyzeLabelAsync(
        byte[]            imageBytes,
        string            mimeType,
        CancellationToken ct)
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("GeminiService: GEMINI_API_KEY is not configured");
            return null;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiService: HTTP request to Gemini failed");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GeminiService: Gemini returned {Status}: {Body}", response.StatusCode, errorBody);
            return null;
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
            return null;
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

            return extraction;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "GeminiService: JSON parsing failed. Raw: {Raw}", rawJson);
            return null;
        }
    }
}
