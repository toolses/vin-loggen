using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Endpoints;

public static class AdminCorrectionEndpoints
{
    public static IEndpointRouteBuilder MapAdminCorrectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/corrections")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", ListCorrections)
            .WithName("AdminListCorrections")
            .WithSummary("List all data corrections with search, filter, and pagination");

        group.MapGet("/{id:guid}", GetCorrection)
            .WithName("AdminGetCorrection")
            .WithSummary("Get a single data correction with full detail");

        group.MapDelete("/{id:guid}", DeleteCorrection)
            .WithName("AdminDeleteCorrection")
            .WithSummary("Delete a data correction record");

        return app;
    }

    private static async Task<Ok<PaginatedResult<AdminCorrectionListItem>>> ListCorrections(
        string?          source,
        string?          search,
        int?             page,
        int?             pageSize,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        var p    = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var offset = (p - 1) * size;

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var where = "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(source))
            where += " AND dc.source = @Source";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND dc.comment ILIKE '%' || @Search || '%'";

        var countSql = $"SELECT COUNT(*) FROM data_corrections dc {where}";
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql,
            new { Source = source, Search = search?.Trim() });

        var sql = $"""
            SELECT
                dc.id                                        AS Id,
                dc.user_id                                   AS UserId,
                u.email                                      AS UserEmail,
                dc.wine_id                                   AS WineId,
                w.name                                       AS WineName,
                w.producer                                   AS WineProducer,
                dc.source                                    AS Source,
                LEFT(dc.comment, 100)                        AS Comment,
                dc.created_at                                AS CreatedAt,
                (SELECT COUNT(*)::int FROM jsonb_object_keys(dc.corrected_data)) AS FieldCount
            FROM data_corrections dc
            LEFT JOIN wines w       ON w.id = dc.wine_id
            LEFT JOIN auth.users u  ON u.id = dc.user_id
            {where}
            ORDER BY dc.created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        var items = await conn.QueryAsync<AdminCorrectionListItem>(sql,
            new { Source = source, Search = search?.Trim(), Limit = size, Offset = offset });

        return TypedResults.Ok(new PaginatedResult<AdminCorrectionListItem>(items, totalCount, p, size));
    }

    private static async Task<Results<Ok<AdminCorrectionDetail>, ProblemHttpResult>> GetCorrection(
        Guid             id,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var correction = await conn.QuerySingleOrDefaultAsync<AdminCorrectionDetail>(
            """
            SELECT
                dc.id                        AS Id,
                dc.user_id                   AS UserId,
                u.email                      AS UserEmail,
                dc.wine_id                   AS WineId,
                w.name                       AS WineName,
                w.producer                   AS WineProducer,
                dc.source                    AS Source,
                dc.original_data::text       AS OriginalData,
                dc.corrected_data::text      AS CorrectedData,
                dc.comment                   AS Comment,
                dc.created_at                AS CreatedAt
            FROM data_corrections dc
            LEFT JOIN wines w       ON w.id = dc.wine_id
            LEFT JOIN auth.users u  ON u.id = dc.user_id
            WHERE dc.id = @Id
            """,
            new { Id = id });

        if (correction is null)
        {
            return TypedResults.Problem(
                detail: "Correction not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(correction);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteCorrection(
        Guid             id,
        NpgsqlDataSource dataSource,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            var deleted = await conn.ExecuteAsync(
                "DELETE FROM data_corrections WHERE id = @Id",
                new { Id = id });

            if (deleted == 0)
            {
                return TypedResults.Problem(
                    detail: "Correction not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return TypedResults.NoContent();
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "Database error in DeleteCorrection for {Id}", id);
            return TypedResults.Problem(
                detail: "Database unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
