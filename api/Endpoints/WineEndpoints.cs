using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Endpoints;

public static class WineEndpoints
{
    public static IEndpointRouteBuilder MapWineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wines").WithTags("Wines");

        group.MapGet("/", GetAllWines)
            .WithName("GetWines")
            .WithSummary("List all logged wines, newest first");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<WineRecord>>, ProblemHttpResult>> GetAllWines(
        NpgsqlDataSource dataSource,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("GetWines called");

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
                    created_at AS CreatedAt
                FROM wines
                ORDER BY created_at DESC
                """
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
