using Dapper;
using Npgsql;

namespace VinLoggen.Api.Services;

public interface IApiUsageService
{
    Task LogAsync(
        string provider,
        string endpoint,
        int?   statusCode,
        int    responseTimeMs,
        Guid?  userId,
        CancellationToken ct,
        string? requestBody    = null,
        string? responseBody   = null,
        Guid?   correlationId  = null,
        string? usedModel      = null,
        int?    totalTokensUsed = null);
}

public sealed class ApiUsageService : IApiUsageService
{
    /// <summary>Bodies are truncated to this length before storage.</summary>
    private const int MaxBodyLength = 4000;

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<ApiUsageService> _logger;

    public ApiUsageService(NpgsqlDataSource dataSource, ILogger<ApiUsageService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task LogAsync(
        string provider,
        string endpoint,
        int?   statusCode,
        int    responseTimeMs,
        Guid?  userId,
        CancellationToken ct,
        string? requestBody    = null,
        string? responseBody   = null,
        Guid?   correlationId  = null,
        string? usedModel      = null,
        int?    totalTokensUsed = null)
    {
        try
        {
            var reqBody  = Truncate(requestBody);
            var respBody = Truncate(responseBody);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO api_usage_logs
                    (provider, endpoint, status_code, response_time_ms, user_id,
                     request_body, response_body, correlation_id,
                     used_model, total_tokens_used)
                VALUES
                    (@Provider, @Endpoint, @StatusCode, @ResponseTimeMs, @UserId,
                     @RequestBody, @ResponseBody, @CorrelationId,
                     @UsedModel, @TotalTokensUsed)
                """,
                new
                {
                    Provider        = provider,
                    Endpoint        = endpoint,
                    StatusCode      = statusCode,
                    ResponseTimeMs  = responseTimeMs,
                    UserId          = userId,
                    RequestBody     = reqBody,
                    ResponseBody    = respBody,
                    CorrelationId   = correlationId,
                    UsedModel       = usedModel,
                    TotalTokensUsed = totalTokensUsed,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ApiUsageService: failed to log {Provider} call", provider);
        }
    }

    private static string? Truncate(string? value)
        => value is not null && value.Length > MaxBodyLength
            ? value[..MaxBodyLength] + "…[truncated]"
            : value;
}
