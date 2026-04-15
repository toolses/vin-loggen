using Dapper;
using Npgsql;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// Tracks Groq token consumption in a 60-second sliding window using
/// <c>api_usage_logs</c>.  When recent usage approaches the 6 000 TPM
/// limit the chain can proactively skip Groq and go straight to the
/// fallback provider, avoiding wasted 429 round-trips.
/// </summary>
public sealed class GroqTokenBudgetService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _budgetPerMinute;
    private readonly ILogger<GroqTokenBudgetService> _logger;

    public GroqTokenBudgetService(
        NpgsqlDataSource dataSource,
        IntegrationSettings settings,
        ILogger<GroqTokenBudgetService> logger)
    {
        _dataSource = dataSource;
        _budgetPerMinute = settings.AiFallback.GroqTokenBudgetPerMinute;
        _logger = logger;
    }

    /// <summary>
    /// Returns the number of tokens remaining in the current 60-second window.
    /// A negative or zero value means Groq should be skipped.
    /// </summary>
    public async Task<int> GetRemainingBudgetAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            var used = await conn.QuerySingleAsync<int>(
                """
                SELECT COALESCE(SUM(total_tokens_used), 0)
                FROM api_usage_logs
                WHERE provider = 'groq'
                  AND created_at > NOW() - INTERVAL '60 seconds'
                """);

            var remaining = _budgetPerMinute - used;
            if (remaining <= 0)
                _logger.LogInformation(
                    "GroqTokenBudget: budget exhausted ({Used}/{Budget} tokens in last 60s)",
                    used, _budgetPerMinute);

            return remaining;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GroqTokenBudget: failed to query budget, assuming available");
            return _budgetPerMinute; // fail-open: assume Groq is available
        }
    }

    /// <summary>
    /// Returns true when there is enough budget remaining for a typical expert
    /// chat request (~3500 tokens).
    /// </summary>
    public async Task<bool> HasBudgetAsync(CancellationToken ct, int estimatedTokens = 3500)
        => await GetRemainingBudgetAsync(ct) >= estimatedTokens;
}
