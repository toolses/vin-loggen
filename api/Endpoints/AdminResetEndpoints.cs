using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;

namespace VinLoggen.Api.Endpoints;

public static class AdminResetEndpoints
{
    public static IEndpointRouteBuilder MapAdminResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/reset")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapDelete("/", ResetData)
            .WithName("AdminResetData")
            .WithSummary("Delete all wines, wine logs, and label storage objects");

        return app;
    }

    private static async Task<Results<Ok<object>, ProblemHttpResult>> ResetData(
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            var deletedWineLogs = await conn.ExecuteAsync("DELETE FROM wine_logs;");
            var deletedWines = await conn.ExecuteAsync("DELETE FROM wines;");

            return TypedResults.Ok<object>(new
            {
                deletedWineLogs,
                deletedWines,
            });
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(
                detail: $"Reset failed: {ex.Message}",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
