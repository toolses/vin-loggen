using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Endpoints;

public static class AdminWineEndpoints
{
    public static IEndpointRouteBuilder MapAdminWineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/wines")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", ListWines)
            .WithName("AdminListWines")
            .WithSummary("List all wines with search, filter, and pagination");

        group.MapGet("/{id:guid}", GetWine)
            .WithName("AdminGetWine")
            .WithSummary("Get a single wine with full detail");

        group.MapPut("/{id:guid}", UpdateWine)
            .WithName("AdminUpdateWine")
            .WithSummary("Update a wine's master data");

        return app;
    }

    private static async Task<Results<Ok<PaginatedResult<AdminWineListItem>>, ProblemHttpResult>> ListWines(
        string? search,
        string? type,
        string? country,
        int? page,
        int? pageSize,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        var pg = Math.Max(page ?? 1, 1);
        var ps = Math.Clamp(pageSize ?? 25, 1, 100);
        var offset = (pg - 1) * ps;

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var countSql = """
            SELECT COUNT(*)::INT
            FROM wines w
            WHERE (@Search IS NULL
                   OR w.name     ILIKE '%' || @Search || '%'
                   OR w.producer ILIKE '%' || @Search || '%'
                   OR w.country  ILIKE '%' || @Search || '%'
                   OR w.region   ILIKE '%' || @Search || '%')
              AND (@Type IS NULL OR w.type = @Type)
              AND (@Country IS NULL OR w.country ILIKE @Country)
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, new
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Type = string.IsNullOrWhiteSpace(type) ? null : type.Trim(),
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim()
        });

        var dataSql = """
            SELECT w.id         AS Id,
                   w.name       AS Name,
                   w.producer   AS Producer,
                   w.vintage    AS Vintage,
                   w.type       AS Type,
                   w.country    AS Country,
                   w.region     AS Region,
                   (SELECT COUNT(*)::INT FROM wine_logs wl WHERE wl.wine_id = w.id) AS LogCount,
                   w.created_at AS CreatedAt
            FROM wines w
            WHERE (@Search IS NULL
                   OR w.name     ILIKE '%' || @Search || '%'
                   OR w.producer ILIKE '%' || @Search || '%'
                   OR w.country  ILIKE '%' || @Search || '%'
                   OR w.region   ILIKE '%' || @Search || '%')
              AND (@Type IS NULL OR w.type = @Type)
              AND (@Country IS NULL OR w.country ILIKE @Country)
            ORDER BY w.created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var items = await conn.QueryAsync<AdminWineListItem>(dataSql, new
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Type = string.IsNullOrWhiteSpace(type) ? null : type.Trim(),
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim(),
            PageSize = ps,
            Offset = offset
        });

        return TypedResults.Ok(new PaginatedResult<AdminWineListItem>(items, totalCount, pg, ps));
    }

    private static async Task<Results<Ok<AdminWineDetail>, ProblemHttpResult>> GetWine(
        Guid id,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var wine = await conn.QuerySingleOrDefaultAsync<AdminWineDetail>(
            """
            SELECT w.id                AS Id,
                   w.name              AS Name,
                   w.producer          AS Producer,
                   w.vintage           AS Vintage,
                   w.type              AS Type,
                   w.country           AS Country,
                   w.region            AS Region,
                   w.grapes            AS Grapes,
                   w.alcohol_content   AS AlcoholContent,
                   w.external_source_id AS ExternalSourceId,
                   w.food_pairings     AS FoodPairings,
                   w.description       AS Description,
                   w.technical_notes   AS TechnicalNotes,
                   (SELECT COUNT(*)::INT FROM wine_logs wl WHERE wl.wine_id = w.id) AS LogCount,
                   w.created_at        AS CreatedAt
            FROM wines w
            WHERE w.id = @Id
            """,
            new { Id = id });

        if (wine is null)
        {
            return TypedResults.Problem(
                detail: $"Wine {id} not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(wine);
    }

    private static async Task<Results<Ok<AdminWineDetail>, ProblemHttpResult>> UpdateWine(
        Guid id,
        AdminWineUpdateRequest request,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var rowsAffected = await conn.ExecuteAsync(
            """
            UPDATE wines SET
                name            = @Name,
                producer        = @Producer,
                vintage         = @Vintage,
                type            = @Type,
                country         = @Country,
                region          = @Region,
                grapes          = @Grapes,
                alcohol_content = @AlcoholContent,
                food_pairings   = @FoodPairings,
                description     = @Description,
                technical_notes = @TechnicalNotes
            WHERE id = @Id
            """,
            new
            {
                Id = id,
                request.Name,
                request.Producer,
                request.Vintage,
                request.Type,
                request.Country,
                request.Region,
                request.Grapes,
                request.AlcoholContent,
                request.FoodPairings,
                request.Description,
                request.TechnicalNotes
            });

        if (rowsAffected == 0)
        {
            return TypedResults.Problem(
                detail: $"Wine {id} not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // Return the updated wine
        var wine = await conn.QuerySingleAsync<AdminWineDetail>(
            """
            SELECT w.id                AS Id,
                   w.name              AS Name,
                   w.producer          AS Producer,
                   w.vintage           AS Vintage,
                   w.type              AS Type,
                   w.country           AS Country,
                   w.region            AS Region,
                   w.grapes            AS Grapes,
                   w.alcohol_content   AS AlcoholContent,
                   w.external_source_id AS ExternalSourceId,
                   w.food_pairings     AS FoodPairings,
                   w.description       AS Description,
                   w.technical_notes   AS TechnicalNotes,
                   (SELECT COUNT(*)::INT FROM wine_logs wl WHERE wl.wine_id = w.id) AS LogCount,
                   w.created_at        AS CreatedAt
            FROM wines w
            WHERE w.id = @Id
            """,
            new { Id = id });

        return TypedResults.Ok(wine);
    }
}
