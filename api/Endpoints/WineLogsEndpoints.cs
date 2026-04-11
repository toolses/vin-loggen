using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Endpoints;

public static class WineLogsEndpoints
{
    public static IEndpointRouteBuilder MapWineLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wines")
            .WithTags("Wines")
            .RequireAuthorization();

        group.MapGet("/{id:guid}/logs", GetWineLogs)
            .WithName("GetWineLogs")
            .WithSummary("Return all tasting logs for a specific wine, newest first");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<WineLogRecord>>, ProblemHttpResult>> GetWineLogs(
        Guid             id,
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

        logger.LogInformation("GetWineLogs: wine {WineId} for user {UserId}", id, userId);

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            var logs = await conn.QueryAsync<WineLogRecord>(
                """
                SELECT
                    id            AS Id,
                    wine_id       AS WineId,
                    user_id       AS UserId,
                    rating        AS Rating,
                    notes         AS Notes,
                    image_url     AS ImageUrl,
                    tasted_at     AS TastedAt,
                    location_name AS LocationName,
                    location_lat  AS LocationLat,
                    location_lng  AS LocationLng,
                    location_type AS LocationType,
                    created_at    AS CreatedAt
                FROM wine_logs
                WHERE wine_id = @WineId
                  AND user_id = @UserId
                ORDER BY created_at DESC
                """,
                new { WineId = id, UserId = userId }
            );

            return TypedResults.Ok(logs);
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "Database error in GetWineLogs");
            return TypedResults.Problem(
                detail: "Database unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
