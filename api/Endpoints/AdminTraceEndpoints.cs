using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;

namespace VinLoggen.Api.Endpoints;

public static class AdminTraceEndpoints
{
    public static IEndpointRouteBuilder MapAdminTraceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/traces")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", GetTraces)
            .WithName("GetTraces")
            .WithSummary("List API call groups by correlation ID");

        group.MapGet("/{correlationId:guid}", GetTraceDetail)
            .WithName("GetTraceDetail")
            .WithSummary("Get all API calls for a specific correlation ID");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<TraceSummary>>, ProblemHttpResult>> GetTraces(
        int? days,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        var lookback = Math.Clamp(days ?? 7, 1, 90);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TraceSummary>(
            """
            SELECT correlation_id                                AS CorrelationId,
                   MIN(created_at)                               AS StartedAt,
                   MAX(created_at)                               AS EndedAt,
                   COUNT(*)::INT                                 AS TotalCalls,
                   COALESCE(SUM(response_time_ms), 0)::INT       AS TotalDurationMs,
                   COALESCE(SUM(total_tokens_used), 0)::INT      AS TotalTokens,
                   COUNT(*) FILTER (WHERE status_code >= 400)::INT AS ErrorCount,
                   ARRAY_AGG(DISTINCT provider)                  AS Providers,
                   ARRAY_AGG(DISTINCT endpoint)                  AS Endpoints,
                   ARRAY_AGG(DISTINCT used_model) FILTER (WHERE used_model IS NOT NULL) AS Models
            FROM api_usage_logs
            WHERE correlation_id IS NOT NULL
              AND created_at >= CURRENT_DATE - @Days * INTERVAL '1 day'
            GROUP BY correlation_id
            ORDER BY MIN(created_at) DESC
            """,
            new { Days = lookback });
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<IEnumerable<TraceEntry>>, ProblemHttpResult>> GetTraceDetail(
        Guid correlationId,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TraceEntry>(
            """
            SELECT id                                        AS Id,
                   provider                                  AS Provider,
                   endpoint                                  AS Endpoint,
                   COALESCE(used_model, '')                   AS UsedModel,
                   COALESCE(status_code, 0)::INT              AS StatusCode,
                   COALESCE(response_time_ms, 0)::INT         AS ResponseTimeMs,
                   COALESCE(total_tokens_used, 0)::INT        AS TotalTokensUsed,
                   COALESCE(request_body, '')                 AS RequestBody,
                   COALESCE(response_body, '')                AS ResponseBody,
                   created_at                                AS CreatedAt
            FROM api_usage_logs
            WHERE correlation_id = @CorrelationId
            ORDER BY created_at ASC
            """,
            new { CorrelationId = correlationId });

        return TypedResults.Ok(rows);
    }
}

public record TraceSummary(
    Guid CorrelationId,
    DateTime StartedAt,
    DateTime EndedAt,
    int TotalCalls,
    int TotalDurationMs,
    int TotalTokens,
    int ErrorCount,
    string[] Providers,
    string[] Endpoints,
    string[]? Models);

public record TraceEntry(
    Guid Id,
    string Provider,
    string Endpoint,
    string UsedModel,
    int StatusCode,
    int ResponseTimeMs,
    int TotalTokensUsed,
    string RequestBody,
    string ResponseBody,
    DateTime CreatedAt);
