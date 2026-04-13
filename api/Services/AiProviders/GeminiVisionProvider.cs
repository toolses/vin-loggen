using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// Vision provider that calls Gemini generateContent with inline image data.
/// Extracted from GeminiService to fit the provider pattern; GeminiService
/// still exists and delegates to this for label scanning.
/// </summary>
public sealed class GeminiVisionProvider : IAiVisionProvider
{
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite-preview:generateContent";

    public string Name => "Gemini";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IApiUsageService _apiUsage;
    private readonly ILogger<GeminiVisionProvider> _logger;

    public GeminiVisionProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IApiUsageService apiUsage,
        ILogger<GeminiVisionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _apiUsage = apiUsage;
        _logger = logger;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_configuration["GEMINI_API_KEY"]);

    public async Task<AiVisionResult> AnalyzeImageAsync(
        string systemPrompt, byte[] imageBytes, string mimeType, CancellationToken ct)
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiVisionResult(null, Name, false);

        var base64 = Convert.ToBase64String(imageBytes);
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = systemPrompt },
                        new { inline_data = new { mime_type = mimeType, data = base64 } },
                    }
                }
            }
        };

        return await SendAsync(apiKey, payload, "AnalyzeLabel", ct);
    }

    public async Task<AiVisionResult> AnalyzeImagesAsync(
        string systemPrompt,
        byte[] frontImageBytes, string frontMimeType,
        byte[]? backImageBytes, string? backMimeType,
        CancellationToken ct)
    {
        if (backImageBytes is null || backImageBytes.Length == 0)
            return await AnalyzeImageAsync(systemPrompt, frontImageBytes, frontMimeType, ct);

        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiVisionResult(null, Name, false);

        var frontBase64 = Convert.ToBase64String(frontImageBytes);
        var backBase64 = Convert.ToBase64String(backImageBytes);
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = systemPrompt },
                        new { inline_data = new { mime_type = frontMimeType, data = frontBase64 } },
                        new { inline_data = new { mime_type = backMimeType!, data = backBase64 } },
                    }
                }
            }
        };

        return await SendAsync(apiKey, payload, "AnalyzeLabels", ct);
    }

    private async Task<AiVisionResult> SendAsync(string apiKey, object payload, string operation, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("gemini");
        HttpResponseMessage response;
        var sw = Stopwatch.StartNew();

        try
        {
            response = await client.PostAsJsonAsync($"{GeminiEndpoint}?key={apiKey}", payload, ct);
            sw.Stop();
            _ = _apiUsage.LogAsync("gemini", operation, (int)response.StatusCode, (int)sw.ElapsedMilliseconds, null, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("gemini", operation, null, (int)sw.ElapsedMilliseconds, null, ct);
            _logger.LogWarning(ex, "GeminiVisionProvider: {Op} timed out", operation);
            return new AiVisionResult(null, Name, false) { IsTransient = true };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("gemini", operation, null, (int)sw.ElapsedMilliseconds, null, ct);
            _logger.LogWarning(ex, "GeminiVisionProvider: {Op} failed", operation);
            return new AiVisionResult(null, Name, false) { IsTransient = true };
        }

        if (response.StatusCode is HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.BadGateway)
        {
            _logger.LogWarning("GeminiVisionProvider: transient {Status} for {Op}", response.StatusCode, operation);
            return new AiVisionResult(null, Name, false) { IsTransient = true };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GeminiVisionProvider: {Status} for {Op}: {Body}", response.StatusCode, operation, body);
            return new AiVisionResult(null, Name, false);
        }

        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var rawText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            return new AiVisionResult(rawText.Trim(), Name, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiVisionProvider: failed to parse {Op} response", operation);
            return new AiVisionResult(null, Name, false);
        }
    }
}
