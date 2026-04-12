using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Endpoints;

public static class WineEndpoints
{
    public static IEndpointRouteBuilder MapWineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wines")
            .WithTags("Wines")
            .RequireAuthorization();

        group.MapGet("/", GetAllWines)
            .WithName("GetWines")
            .WithSummary("List all logged wines for the authenticated user (latest log per wine), newest first");

        group.MapGet("/search", SearchWines)
            .WithName("SearchWines")
            .WithSummary("Search the global wine catalogue by name or producer");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<WineLogRecord>>, ProblemHttpResult>> GetAllWines(
        ClaimsPrincipal  user,
        NpgsqlDataSource dataSource,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.FindFirstValue("sub");

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Problem(
                detail: "Could not resolve user identity from token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        logger.LogInformation("GetWines called for user {UserId}", userId);

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            // Returns the most-recent wine_log per wine for this user, joined
            // with the wine master record for display metadata.
            var logs = await conn.QueryAsync<WineLogRecord>(
                """
                SELECT DISTINCT ON (wl.wine_id)
                    wl.id            AS Id,
                    wl.wine_id       AS WineId,
                    wl.user_id       AS UserId,
                    wl.rating        AS Rating,
                    wl.notes         AS Notes,
                    wl.image_url     AS ImageUrl,
                    wl.tasted_at     AS TastedAt,
                    wl.location_name AS LocationName,
                    wl.location_lat  AS LocationLat,
                    wl.location_lng  AS LocationLng,
                    wl.location_type AS LocationType,
                    wl.created_at    AS CreatedAt
                FROM wine_logs wl
                WHERE wl.user_id = @UserId
                ORDER BY wl.wine_id, wl.created_at DESC
                """,
                new { UserId = userId }
            );

            return TypedResults.Ok(logs);
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "Database error in GetWines");
            return TypedResults.Problem(
                detail: "Database unavailable. Check SUPABASE_CONNECTION_STRING.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<Ok<IEnumerable<WineSearchResult>>> SearchWines(
        string? q,
        int?    limit,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return TypedResults.Ok(Enumerable.Empty<WineSearchResult>());
        }

        var search = q.Trim();
        var max = Math.Clamp(limit ?? 10, 1, 20);

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var results = await conn.QueryAsync<WineSearchResult>(
            """
            SELECT w.id              AS Id,
                   w.name            AS Name,
                   w.producer        AS Producer,
                   w.vintage         AS Vintage,
                   w.type            AS Type,
                   w.country         AS Country,
                   w.region          AS Region,
                   w.grapes          AS Grapes,
                   w.alcohol_content AS AlcoholContent
            FROM wines w
            WHERE w.name     ILIKE '%' || @Search || '%'
               OR w.producer ILIKE '%' || @Search || '%'
            ORDER BY
                CASE WHEN LOWER(w.name) = LOWER(@Search) OR LOWER(w.producer) = LOWER(@Search)
                     THEN 0 ELSE 1 END,
                w.name
            LIMIT @Limit
            """,
            new { Search = search, Limit = max });

        return TypedResults.Ok(results);
    }
}
