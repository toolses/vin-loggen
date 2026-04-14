using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// AI chat provider that calls the DeepSeek API (OpenAI-compatible format).
/// Model: deepseek-chat (DeepSeek-V3).
/// </summary>
public sealed class DeepSeekChatProvider : IAiChatProvider
{
    public string Name => "DeepSeek";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IApiUsageService _apiUsage;
    private readonly ILogger<DeepSeekChatProvider> _logger;
    private readonly string _baseUrl;

    public DeepSeekChatProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IApiUsageService apiUsage,
        ILogger<DeepSeekChatProvider> logger,
        VinLoggen.Api.Configuration.IntegrationSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _apiUsage = apiUsage;
        _logger = logger;
        _baseUrl = settings.DeepSeek.BaseUrl.TrimEnd('/');
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_configuration["DEEPSEEK_API_KEY"]);

    public async Task<AiChatResult> ChatAsync(
        string systemPrompt,
        string userContent,
        CancellationToken ct,
        Guid? userId        = null,
        Guid? correlationId = null)
    {
        var apiKey = _configuration["DEEPSEEK_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("DeepSeekChatProvider: DEEPSEEK_API_KEY is not configured");
            return new AiChatResult(null, Name, false);
        }

        var payload = new DeepSeekRequest(
            Model: "deepseek-chat",
            Messages:
            [
                new("system", systemPrompt),
                new("user", userContent),
            ],
            Temperature: 0.7,
            MaxTokens: 2048
        );

        var client = _httpClientFactory.CreateClient("deepseek");

        HttpResponseMessage response;
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
            {
                Content = JsonContent.Create(payload, options: JsonOpts)
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            response = await client.SendAsync(request, ct);
            sw.Stop();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("deepseek", "ExpertChat", null, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: userContent, correlationId: correlationId, usedModel: "DS");
            _logger.LogWarning(ex, "DeepSeekChatProvider: request timed out");
            return new AiChatResult(null, Name, false) { IsTransient = true, UsedModel = "DS" };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = _apiUsage.LogAsync("deepseek", "ExpertChat", null, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: userContent, correlationId: correlationId, usedModel: "DS");
            _logger.LogWarning(ex, "DeepSeekChatProvider: request failed");
            return new AiChatResult(null, Name, false) { IsTransient = true, UsedModel = "DS" };
        }

        if (response.StatusCode is HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.BadGateway)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("deepseek", "ExpertChat", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: userContent, responseBody: body, correlationId: correlationId, usedModel: "DS");
            _logger.LogWarning("DeepSeekChatProvider: transient {Status}: {Body}", response.StatusCode, body);
            return new AiChatResult(null, Name, false) { IsTransient = true, UsedModel = "DS" };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _ = _apiUsage.LogAsync("deepseek", "ExpertChat", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: userContent, responseBody: body, correlationId: correlationId, usedModel: "DS");
            _logger.LogError("DeepSeekChatProvider: {Status}: {Body}", response.StatusCode, body);
            return new AiChatResult(null, Name, false) { UsedModel = "DS" };
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<DeepSeekResponse>(JsonOpts, ct);
            var text = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            var totalTokens = result?.Usage?.TotalTokens;

            if (string.IsNullOrWhiteSpace(text))
            {
                _ = _apiUsage.LogAsync("deepseek", "ExpertChat", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                    userId, ct, requestBody: userContent, correlationId: correlationId, usedModel: "DS",
                    totalTokensUsed: totalTokens);
                _logger.LogWarning("DeepSeekChatProvider: empty response");
                return new AiChatResult(null, Name, false) { UsedModel = "DS", TotalTokensUsed = totalTokens };
            }

            _ = _apiUsage.LogAsync("deepseek", "ExpertChat", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: userContent, responseBody: text, correlationId: correlationId,
                usedModel: "DS", totalTokensUsed: totalTokens);

            _logger.LogInformation("DeepSeekChatProvider: success ({Ms}ms, {Tokens} tokens)",
                sw.ElapsedMilliseconds, totalTokens);
            return new AiChatResult(text, Name, true) { UsedModel = "DS", TotalTokensUsed = totalTokens };
        }
        catch (Exception ex)
        {
            _ = _apiUsage.LogAsync("deepseek", "ExpertChat", (int)response.StatusCode, (int)sw.ElapsedMilliseconds,
                userId, ct, requestBody: userContent, correlationId: correlationId, usedModel: "DS");
            _logger.LogError(ex, "DeepSeekChatProvider: failed to parse response");
            return new AiChatResult(null, Name, false) { UsedModel = "DS" };
        }
    }

    // ── OpenAI-compatible request/response DTOs ─────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private record DeepSeekRequest(
        string Model,
        DeepSeekMessage[] Messages,
        double? Temperature = null,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens = null);

    private record DeepSeekMessage(string Role, string Content);

    private record DeepSeekResponse(DeepSeekChoice[]? Choices, DeepSeekUsage? Usage);
    private record DeepSeekChoice(DeepSeekMessage? Message);
    private record DeepSeekUsage(
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);
}
