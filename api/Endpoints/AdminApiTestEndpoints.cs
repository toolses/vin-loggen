using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Endpoints;

public static class AdminApiTestEndpoints
{
    public static IEndpointRouteBuilder MapAdminApiTestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/api-test")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/wineapi/search", SearchWineApi)
            .WithName("AdminTestWineApiSearch")
            .WithSummary("Test WineAPI search with free-text query (producer + name + vintage)");

        return app;
    }

    private static async Task<Results<Ok<WineApiTestResult>, ProblemHttpResult>> SearchWineApi(
        string producer,
        string name,
        int? vintage,
        ClaimsPrincipal user,
        IWineApiService wineApiService,
        CancellationToken ct)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.FindFirstValue("sub");
        Guid? userId = Guid.TryParse(userIdClaim, out var id) ? id : null;

        var correlationId = Guid.NewGuid();

        var enrichment = await wineApiService.FindAsync(
            producer, name, vintage, ct, userId, correlationId);

        return TypedResults.Ok(new WineApiTestResult(
            CorrelationId: correlationId,
            Found: enrichment is not null,
            ExternalId: enrichment?.ExternalId,
            SuggestedName: enrichment?.SuggestedName,
            SuggestedProducer: enrichment?.SuggestedProducer,
            Description: enrichment?.Description,
            FoodPairings: enrichment?.FoodPairings,
            TechnicalNotes: enrichment?.TechnicalNotes,
            AlcoholContent: enrichment?.AlcoholContent,
            Grapes: enrichment?.Grapes));
    }
}

public record WineApiTestResult(
    Guid CorrelationId,
    bool Found,
    string? ExternalId,
    string? SuggestedName,
    string? SuggestedProducer,
    string? Description,
    string[]? FoodPairings,
    string? TechnicalNotes,
    double? AlcoholContent,
    string[]? Grapes);
