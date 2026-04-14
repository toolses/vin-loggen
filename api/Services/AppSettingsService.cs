using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace VinLoggen.Api.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private const string CachePrefix = "appsetting:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IMemoryCache _cache;

    public AppSettingsService(NpgsqlDataSource dataSource, IMemoryCache cache)
    {
        _dataSource = dataSource;
        _cache = cache;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var cacheKey = CachePrefix + key;
        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var value = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM app_settings WHERE key = @Key",
            new { Key = key });

        if (value is not null)
            _cache.Set(cacheKey, value, CacheTtl);

        return value;
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(string Key, string Value)>(
            "SELECT key, value FROM app_settings ORDER BY key");

        return rows.ToDictionary(r => r.Key, r => r.Value);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@Key, @Value, NOW())
            ON CONFLICT (key) DO UPDATE SET value = @Value, updated_at = NOW()
            """,
            new { Key = key, Value = value });

        _cache.Set(CachePrefix + key, value, CacheTtl);
    }
}
