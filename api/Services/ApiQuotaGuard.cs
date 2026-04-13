using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace VinLoggen.Api.Services;

/// <summary>
/// Shared helper for enforcing hard daily API request limits.
/// Counts rows in <c>api_usage_logs</c> for the current UTC date
/// and caches the result for 30 seconds to avoid a DB round-trip on every call.
/// </summary>
internal static class ApiQuotaGuard
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns <c>true</c> when today's request count for <paramref name="provider"/>
    /// has reached or exceeded <paramref name="maxDaily"/>.
    /// Pass <c>0</c> for <paramref name="maxDaily"/> to disable the cap (unlimited).
    /// Fail-safe: blocks on DB error.
    /// </summary>
    public static async Task<bool> IsDailyQuotaExceededAsync(
        string           provider,
        int              maxDaily,
        IMemoryCache     cache,
        NpgsqlDataSource dataSource,
        ILogger          logger,
        CancellationToken ct)
    {
        if (maxDaily <= 0) return false;

        var cacheKey = $"{provider}:daily-request-count";

        if (!cache.TryGetValue<long>(cacheKey, out var count))
        {
            try
            {
                await using var conn = await dataSource.OpenConnectionAsync(ct);
                count = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM api_usage_logs WHERE provider = @Provider AND created_at >= CURRENT_DATE",
                    new { Provider = provider });
                cache.Set(cacheKey, count, CacheTtl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ApiQuotaGuard: quota check failed for '{Provider}' — blocking request", provider);
                return true;
            }
        }

        if (count < maxDaily) return false;

        logger.LogCritical(
            "ApiQuotaGuard: daily quota of {Max} requests reached for '{Provider}' (today: {Count}). " +
            "All {Provider} calls are disabled until tomorrow. " +
            "Raise Integration__{Setting} or set it to 0 to disable the cap.",
            maxDaily, provider, count, provider,
            provider == "gemini" ? "GeminiMaxDailyRequests" : "WineApiMaxDailyRequests");

        return true;
    }

    /// <summary>
    /// Evicts the cached daily count so the next check re-queries the database.
    /// Call this after each real API call.
    /// </summary>
    public static void EvictCache(string provider, IMemoryCache cache)
    {
        cache.Remove($"{provider}:daily-request-count");
    }
}
