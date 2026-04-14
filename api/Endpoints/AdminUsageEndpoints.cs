using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;

namespace VinLoggen.Api.Endpoints;

public static class AdminUsageEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/usage")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/today", GetTodayUsage)
            .WithName("GetTodayUsage")
            .WithSummary("Today's API call counts per provider");

        group.MapGet("/daily", GetDailyUsage)
            .WithName("GetDailyUsage")
            .WithSummary("Daily API call totals per provider over a date range");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<ProviderUsageToday>>, ProblemHttpResult>> GetTodayUsage(
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProviderUsageToday>(
            """
            SELECT provider                                                AS Provider,
                   REGEXP_REPLACE(endpoint,
                       '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}',
                       '{id}', 'gi')                                      AS Endpoint,
                   used_model                                              AS UsedModel,
                   COUNT(*)::INT                                           AS TotalCalls,
                   COALESCE(AVG(response_time_ms), 0)::INT                 AS AvgResponseMs,
                   COUNT(*) FILTER (WHERE status_code >= 400)::INT         AS ErrorCount,
                   COALESCE(SUM(total_tokens_used), 0)::INT                AS TotalTokens
            FROM api_usage_logs
            WHERE created_at >= CURRENT_DATE
            GROUP BY provider, REGEXP_REPLACE(endpoint,
                       '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}',
                       '{id}', 'gi'), used_model
            ORDER BY provider, endpoint
            """);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<IEnumerable<DailyUsageRow>>, ProblemHttpResult>> GetDailyUsage(
        int? days,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        var lookback = Math.Clamp(days ?? 30, 1, 90);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DailyUsageRow>(
            """
            SELECT DATE(created_at)::DATE                                  AS Date,
                   provider                                                AS Provider,
                   REGEXP_REPLACE(endpoint,
                       '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}',
                       '{id}', 'gi')                                      AS Endpoint,
                   used_model                                              AS UsedModel,
                   COUNT(*)::INT                                           AS TotalCalls,
                   COALESCE(AVG(response_time_ms), 0)::INT                 AS AvgResponseMs,
                   COALESCE(SUM(total_tokens_used), 0)::INT                AS TotalTokens
            FROM api_usage_logs
            WHERE created_at >= CURRENT_DATE - @Days * INTERVAL '1 day'
            GROUP BY DATE(created_at), provider, REGEXP_REPLACE(endpoint,
                       '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}',
                       '{id}', 'gi'), used_model
            ORDER BY DATE(created_at) DESC, provider, endpoint
            """,
            new { Days = lookback });
        return TypedResults.Ok(rows);
    }
}

public record ProviderUsageToday(string Provider, string Endpoint, string? UsedModel, int TotalCalls, int AvgResponseMs, int ErrorCount, int TotalTokens);
public record DailyUsageRow(DateTime Date, string Provider, string Endpoint, string? UsedModel, int TotalCalls, int AvgResponseMs, int TotalTokens);
