using Dapper;
using Npgsql;

namespace VinLoggen.Api.Services;

public interface IApiUsageService
{
    Task LogAsync(string provider, string endpoint, int? statusCode,
                  int responseTimeMs, Guid? userId, CancellationToken ct);
}

public sealed class ApiUsageService : IApiUsageService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<ApiUsageService> _logger;

    public ApiUsageService(NpgsqlDataSource dataSource, ILogger<ApiUsageService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task LogAsync(string provider, string endpoint, int? statusCode,
                               int responseTimeMs, Guid? userId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO api_usage_logs (provider, endpoint, status_code, response_time_ms, user_id)
                VALUES (@Provider, @Endpoint, @StatusCode, @ResponseTimeMs, @UserId)
                """,
                new { Provider = provider, Endpoint = endpoint,
                      StatusCode = statusCode, ResponseTimeMs = responseTimeMs, UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ApiUsageService: failed to log {Provider} call", provider);
        }
    }
}
