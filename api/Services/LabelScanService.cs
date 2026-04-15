using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Services;

/// <summary>
/// Abstraction for the label-scan vision pipeline with provider fallback.
/// </summary>
public interface ILabelScanService
{
    Task<GeminiResult<WineAnalysisResponse>> AnalyzeLabelsAsync(
        byte[]            frontImageBytes,
        string            frontMimeType,
        byte[]?           backImageBytes,
        string?           backMimeType,
        CancellationToken ct,
        Guid?             userId        = null,
        Guid?             correlationId = null,
        string?           frontImageUrl = null,
        string?           backImageUrl  = null);
}

/// <summary>
/// Vision fallback chain for label scanning.
/// Tries providers in order from <see cref="AiFallbackSettings.LabelScanPriority"/>
/// (default: Groq Llama 4 Scout → Gemini).
/// </summary>
public sealed class LabelScanService : ILabelScanService
{
    /// <summary>
    /// Norwegian prompt for single/dual-image analysis — same as GeminiService for consistency.
    /// </summary>
    private const string VisionPrompt = """
        Du er en sommelier-ekspert. Analyser bildet/bildene av vinetiketten og returner KUN rå JSON (uten markdown-formatering).
        JSON-struktur:
        {
          "wineName": string,
          "producer": string,
          "vintage": integer,
          "country": string,
          "region": string,
          "grapes": string[],
          "type": "Rød"|"Hvit"|"Musserende"|"Rosé"|"Oransje"|"Dessert",
          "alcoholContent": number,
          "foodPairings": string[]
        }

        VIKTIGE REGLER for konsistens:
        - "producer": Bruk FULLT produsentnavn nøyaktig som det står på etiketten (f.eks. "Markus Molitor", ikke bare "Molitor").
        - "wineName": Vinens eget navn UTEN produsentnavnet. Hvis etiketten bare viser produsent + druesort + region, bruk druesort + region som wineName (f.eks. "Riesling Spätlese Zeltinger Sonnenuhr").
        - Ikke gjenta produsentnavnet i wineName-feltet.
        - Bruk offisielle navn fra etiketten, ikke forkortelser eller omskrivninger.
        - "country": Bruk ALLTID det engelske navnet på landet (f.eks. "France", "Italy", "Spain", "Germany", "Portugal", "South Africa", "New Zealand"). Aldri bruk norske landnavn.
        - "foodPairings": Matanbefalinger fra baketiketten. Hvis ingen finnes, returner null.

        Hvis du er usikker på et felt, sett det til null.
        """;

    private const string GroqModel = "meta-llama/llama-4-scout-17b-16e-instruct";

    private readonly IGeminiService _geminiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IntegrationSettings _settings;
    private readonly IApiUsageService _apiUsage;
    private readonly ILogger<LabelScanService> _logger;

    public LabelScanService(
        IGeminiService geminiService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IntegrationSettings settings,
        IApiUsageService apiUsage,
        ILogger<LabelScanService> logger)
    {
        _geminiService = geminiService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _settings = settings;
        _apiUsage = apiUsage;
        _logger = logger;
    }

    public async Task<GeminiResult<WineAnalysisResponse>> AnalyzeLabelsAsync(
        byte[] frontImageBytes, string frontMimeType,
        byte[]? backImageBytes, string? backMimeType,
        CancellationToken ct, Guid? userId = null, Guid? correlationId = null,
        string? frontImageUrl = null, string? backImageUrl = null)
    {
        foreach (var providerName in _settings.AiFallback.LabelScanPriority)
        {
            GeminiResult<WineAnalysisResponse>? result = providerName.ToLowerInvariant() switch
            {
                "groq" => await TryGroqVisionAsync(frontImageBytes, frontMimeType, backImageBytes, backMimeType, ct, userId, correlationId, frontImageUrl, backImageUrl),
                "gemini" => await TryGeminiAsync(frontImageBytes, frontMimeType, backImageBytes, backMimeType, ct, userId, correlationId, frontImageUrl, backImageUrl),
                _ => null,
            };

            if (result is { IsSuccess: true })
                return result;

            _logger.LogWarning("LabelScanService: {Provider} failed or unavailable, trying next", providerName);
        }

        return new GeminiResult<WineAnalysisResponse>(null, "Alle AI-leverandører feilet for etikett-skanning");
    }

    // ── Groq Vision (Llama 4 Scout) via OpenAI-compatible API ───────────────

    private async Task<GeminiResult<WineAnalysisResponse>?> TryGroqVisionAsync(
        byte[] frontImageBytes, string frontMimeType,
        byte[]? backImageBytes, string? backMimeType,
        CancellationToken ct, Guid? userId, Guid? correlationId,
        string? frontImageUrl, string? backImageUrl)
    {
        var apiKey = _configuration["GROQ_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("LabelScanService: GROQ_API_KEY not configured, skipping Groq vision");
            return null;
        }

        var baseUrl = _settings.Groq.BaseUrl.TrimEnd('/');

        // Build content parts: system text + image(s) as base64 data URLs
        var contentParts = new List<object>
        {
            new { type = "text", text = VisionPrompt },
            new
            {
                type = "image_url",
                image_url = new { url = $"data:{frontMimeType};base64,{Convert.ToBase64String(frontImageBytes)}" }
            }
        };

        if (backImageBytes is { Length: > 0 })
        {
            contentParts.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:{backMimeType};base64,{Convert.ToBase64String(backImageBytes)}" }
            });
        }

        var payload = new
        {
            model = GroqModel,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = contentParts.ToArray()
                }
            },
            temperature = 0.2,
            max_tokens = 1024
        };

        var client = _httpClientFactory.CreateClient("groq");
        var reqSummary = BuildReqSummary(frontMimeType, backImageBytes, backMimeType, frontImageUrl, backImageUrl);

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            var response = await client.SendAsync(request, ct);
            sw.Stop();

            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                _ = _apiUsage.LogAsync("groq", "LabelScan", 429, (int)sw.ElapsedMilliseconds,
                    userId, ct, requestBody: reqSummary, correlationId: correlationId, usedModel: "L4S");
                _logger.LogWarning("LabelScanService: Groq 429 rate limited, immediate fallback");
                return null; // triggers next provider
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _ = _apiUsage.LogAsync("groq", "LabelScan", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                    userId, ct, requestBody: reqSummary, responseBody: errorBody, correlationId: correlationId, usedModel: "L4S");
                _logger.LogWarning("LabelScanService: Groq returned {Status}: {Body}", response.StatusCode, errorBody);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            var totalTokens = doc.RootElement.TryGetProperty("usage", out var usage)
                && usage.TryGetProperty("total_tokens", out var tok)
                ? tok.GetInt32()
                : (int?)null;

            // Strip markdown code fences
            text = Regex.Replace(text.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

            _ = _apiUsage.LogAsync("groq", "LabelScan", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: reqSummary, responseBody: text, correlationId: correlationId,
                usedModel: "L4S", totalTokensUsed: totalTokens);

            var extraction = JsonSerializer.Deserialize<WineAnalysisResponse>(
                text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation("LabelScanService: Groq vision extracted '{WineName}' ({Ms}ms, {Tokens} tokens)",
                extraction?.WineName, sw.ElapsedMilliseconds, totalTokens);

            return new GeminiResult<WineAnalysisResponse>(extraction, null);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("groq", "LabelScan", null, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: reqSummary, correlationId: correlationId, usedModel: "L4S");
            _logger.LogWarning(ex, "LabelScanService: Groq vision timed out");
            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("groq", "LabelScan", null, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: reqSummary, correlationId: correlationId, usedModel: "L4S");
            _logger.LogWarning(ex, "LabelScanService: Groq vision failed");
            return null;
        }
    }

    // ── Gemini fallback — delegates to existing GeminiService ────────────────

    private async Task<GeminiResult<WineAnalysisResponse>?> TryGeminiAsync(
        byte[] frontImageBytes, string frontMimeType,
        byte[]? backImageBytes, string? backMimeType,
        CancellationToken ct, Guid? userId, Guid? correlationId,
        string? frontImageUrl, string? backImageUrl)
    {
        return backImageBytes is { Length: > 0 }
            ? await _geminiService.AnalyzeLabelsAsync(frontImageBytes, frontMimeType, backImageBytes, backMimeType, ct, userId, correlationId, frontImageUrl, backImageUrl)
            : await _geminiService.AnalyzeLabelAsync(frontImageBytes, frontMimeType, ct, userId, correlationId, frontImageUrl);
    }

    /// <summary>
    /// Builds a JSON request summary that includes MIME types and optional Supabase image URLs
    /// for traceability in api_usage_logs.
    /// </summary>
    private static string BuildReqSummary(
        string frontMimeType, byte[]? backImageBytes, string? backMimeType,
        string? frontImageUrl, string? backImageUrl)
    {
        var hasBack = backImageBytes is { Length: > 0 };
        var parts = new List<string> { $"\"mimeTypes\":\"{frontMimeType}{(hasBack ? $"+{backMimeType}" : "")}\"" };
        if (!string.IsNullOrWhiteSpace(frontImageUrl))
            parts.Add($"\"frontImageUrl\":\"{frontImageUrl}\"");
        if (hasBack && !string.IsNullOrWhiteSpace(backImageUrl))
            parts.Add($"\"backImageUrl\":\"{backImageUrl}\"");
        return "{" + string.Join(",", parts) + "}";
    }
}
