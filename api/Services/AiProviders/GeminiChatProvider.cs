using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// AI chat provider that calls the Gemini generateContent API.
/// Used as fallback for expert chat when DeepSeek is unavailable.
/// </summary>
public sealed class GeminiChatProvider : IAiChatProvider
{
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite-preview:generateContent";

    public string Name => "Gemini";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IApiUsageService _apiUsage;
    private readonly ILogger<GeminiChatProvider> _logger;

    public GeminiChatProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IApiUsageService apiUsage,
        ILogger<GeminiChatProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _apiUsage = apiUsage;
        _logger = logger;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_configuration["GEMINI_API_KEY"]);

    public async Task<AiChatResult> ChatAsync(string systemPrompt, string userContent, CancellationToken ct)
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("GeminiChatProvider: GEMINI_API_KEY is not configured");
            return new AiChatResult(null, Name, false);
        }

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = systemPrompt },
                        new { text = userContent },
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient("gemini");

        HttpResponseMessage response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = await client.PostAsJsonAsync($"{GeminiEndpoint}?key={apiKey}", payload, ct);
            sw.Stop();
            _ = _apiUsage.LogAsync("gemini", "ExpertChat", (int)response.StatusCode, (int)sw.ElapsedMilliseconds, null, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("gemini", "ExpertChat", null, (int)sw.ElapsedMilliseconds, null, ct);
            _logger.LogWarning(ex, "GeminiChatProvider: request timed out");
            return new AiChatResult(null, Name, false) { IsTransient = true };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("gemini", "ExpertChat", null, (int)sw.ElapsedMilliseconds, null, ct);
            _logger.LogWarning(ex, "GeminiChatProvider: request failed");
            return new AiChatResult(null, Name, false) { IsTransient = true };
        }

        if (response.StatusCode is HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.BadGateway)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("GeminiChatProvider: transient {Status}: {Body}", response.StatusCode, body);
            return new AiChatResult(null, Name, false) { IsTransient = true };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GeminiChatProvider: {Status}: {Body}", response.StatusCode, body);
            return new AiChatResult(null, Name, false);
        }

        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()?.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("GeminiChatProvider: empty response");
                return new AiChatResult(null, Name, false);
            }

            _logger.LogInformation("GeminiChatProvider: success ({Ms}ms)", sw.ElapsedMilliseconds);
            return new AiChatResult(text, Name, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeminiChatProvider: failed to parse response");
            return new AiChatResult(null, Name, false);
        }
    }
}
