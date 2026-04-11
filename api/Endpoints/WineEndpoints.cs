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
}
