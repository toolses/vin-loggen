using Dapper;
using Npgsql;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Services;

/// <summary>
/// Manages the daily Pro-feature quota for free-tier users.
///
/// Thread-safety: the quota increment uses a single atomic PostgreSQL UPSERT
/// (no lost updates possible). The check is a non-locking read; in the rare
/// event of concurrent requests both passing the read-check, the counter may
/// slightly exceed the daily limit by at most the number of simultaneous
/// in-flight requests. This is acceptable for a soft freemium quota.
/// </summary>
public sealed class ProUsageService : IProUsageService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IntegrationSettings _settings;
    private readonly ILogger<ProUsageService> _logger;

    // Internal Dapper projection
    private record ProfileQuotaRow(string Tier, int ScansToday);

    public record ProStatus(
        bool CanUsePro,
        bool IsPro,
        int  ScansToday,
        int  DailyLimit,
        int  ScansRemaining
    );

    public ProUsageService(
        NpgsqlDataSource         dataSource,
        IntegrationSettings      settings,
        ILogger<ProUsageService> logger)
    {
        _dataSource = dataSource;
        _settings   = settings;
        _logger     = logger;
    }

    /// <summary>
    /// Returns the current Pro status for the user without modifying any counters.
    /// Handles the daily reset: if <c>last_pro_scan_date</c> is before today the
    /// effective scan count is treated as 0 without requiring a prior DB write.
    /// </summary>
    public async Task<ProStatus> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<ProfileQuotaRow>(
            """
            SELECT
                subscription_tier AS Tier,
                -- Virtual daily-reset: treat as 0 if the stored date is before today
                CASE WHEN last_pro_scan_date < CURRENT_DATE THEN 0
                     ELSE pro_scans_today
                END AS ScansToday
            FROM user_profiles
            WHERE user_id = @UserId
            """,
            new { UserId = userId });

        // No profile row yet → treat as free tier with 0 scans used
        if (row is null)
        {
            _logger.LogDebug("ProUsageService: no profile for {UserId}, returning default free status", userId);
            return new ProStatus(true, false, 0, _settings.DailyProLimit, _settings.DailyProLimit);
        }

        bool isPro      = string.Equals(row.Tier, "pro", StringComparison.OrdinalIgnoreCase);
        int  scansToday = row.ScansToday;
        bool canUse     = isPro || scansToday < _settings.DailyProLimit;
        int  remaining  = isPro ? int.MaxValue : Math.Max(0, _settings.DailyProLimit - scansToday);

        _logger.LogDebug(
            "ProUsageService: user {UserId} tier={Tier} scans={Scans}/{Limit} canUse={CanUse}",
            userId, row.Tier, scansToday, _settings.DailyProLimit, canUse);

        return new ProStatus(canUse, isPro, scansToday, _settings.DailyProLimit, remaining);
    }

    /// <summary>
    /// Atomically increments the Pro-scan counter for a free-tier user.
    /// Resets the counter when the stored date is before today.
    /// Does nothing for Pro users (no limit to track).
    /// Creates the profile row if it doesn't exist yet.
    /// </summary>
    public async Task IncrementAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await conn.ExecuteAsync(
            """
            INSERT INTO user_profiles (user_id, pro_scans_today, last_pro_scan_date)
            VALUES (@UserId, 1, CURRENT_DATE)
            ON CONFLICT (user_id) DO UPDATE SET
                pro_scans_today    = CASE
                    WHEN user_profiles.last_pro_scan_date < CURRENT_DATE THEN 1
                    ELSE user_profiles.pro_scans_today + 1
                END,
                last_pro_scan_date = CURRENT_DATE
            WHERE user_profiles.subscription_tier <> 'pro'
            """,
            new { UserId = userId });

        _logger.LogInformation("ProUsageService: incremented Pro scan counter for user {UserId}", userId);
    }
}
