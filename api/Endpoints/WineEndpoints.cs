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
            .WithSummary("List all logged wines for the authenticated user, newest first");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<WineRecord>>, ProblemHttpResult>> GetAllWines(
        ClaimsPrincipal user,
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

            var wines = await conn.QueryAsync<WineRecord>(
                """
                SELECT
                    id,
                    name,
                    producer,
                    vintage,
                    type,
                    country,
                    region,
                    rating,
                    notes,
                    image_url  AS ImageUrl,
                    tasted_at  AS TastedAt,
                    created_at AS CreatedAt,
                    user_id    AS UserId
                FROM wines
                WHERE user_id = @UserId
                ORDER BY created_at DESC
                """,
                new { UserId = userId }
            );

            return TypedResults.Ok(wines);
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
