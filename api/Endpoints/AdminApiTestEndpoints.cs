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
            .WithSummary("Test WineAPI search – returns all hits");

        group.MapGet("/wineapi/details/{wineId}", GetWineApiDetails)
            .WithName("AdminTestWineApiDetails")
            .WithSummary("Test WineAPI – fetch full details for a wine by ID");

        group.MapPost("/wineapi/identify-text", IdentifyByText)
            .WithName("AdminTestWineApiIdentifyText")
            .WithSummary("Test WineAPI identify/text – returns wine + suggestions");

        return app;
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private static async Task<Results<Ok<WineApiSearchTestResult>, ProblemHttpResult>> SearchWineApi(
        string producer,
        string name,
        int? vintage,
        ClaimsPrincipal user,
        IWineApiService wineApiService,
        WineCatalogueService catalogueService,
        CancellationToken ct)
    {
        var userId = ExtractUserId(user);
        var correlationId = Guid.NewGuid();

        var hits = await wineApiService.SearchRawAsync(
            producer, name, vintage, ct, userId, correlationId);

        var savedWines = hits is { Count: > 0 }
            ? await catalogueService.UpsertFromSearchHitsAsync(hits, ct)
            : new Dictionary<string, Guid>();

        return TypedResults.Ok(new WineApiSearchTestResult(
            CorrelationId: correlationId,
            HitCount: hits?.Count ?? 0,
            Hits: hits ?? [],
            SavedWines: savedWines));
    }

    private static async Task<Results<Ok<WineApiDetailTestResult>, ProblemHttpResult>> GetWineApiDetails(
        string wineId,
        ClaimsPrincipal user,
        IWineApiService wineApiService,
        WineCatalogueService catalogueService,
        CancellationToken ct)
    {
        var userId = ExtractUserId(user);
        var correlationId = Guid.NewGuid();

        var detail = await wineApiService.GetDetailsRawAsync(
            wineId, ct, userId, correlationId);

        Guid? savedWineId = detail is not null
            ? await catalogueService.UpsertFromDetailAsync(detail, ct)
            : null;

        return TypedResults.Ok(new WineApiDetailTestResult(
            CorrelationId: correlationId,
            Found: detail is not null,
            Detail: detail,
            SavedWineId: savedWineId));
    }

    private static async Task<Results<Ok<WineApiIdentifyTestResult>, ProblemHttpResult>> IdentifyByText(
        IdentifyTextRequest body,
        ClaimsPrincipal user,
        IWineApiService wineApiService,
        WineCatalogueService catalogueService,
        CancellationToken ct)
    {
        var userId = ExtractUserId(user);
        var correlationId = Guid.NewGuid();

        var result = await wineApiService.IdentifyByTextRawAsync(
            body.Query, ct, userId, correlationId);

        var savedWines = result is not null
            ? await catalogueService.UpsertFromIdentifyHitsAsync(result, ct)
            : new Dictionary<string, Guid>();

        return TypedResults.Ok(new WineApiIdentifyTestResult(
            CorrelationId: correlationId,
            Found: result?.Wine is not null,
            Result: result,
            SavedWines: savedWines));
    }
}

// ── Response wrapper records ─────────────────────────────────────────────

public record WineApiSearchTestResult(
    Guid CorrelationId,
    int HitCount,
    List<WineApiSearchHitDto> Hits,
    Dictionary<string, Guid> SavedWines);

public record WineApiDetailTestResult(
    Guid CorrelationId,
    bool Found,
    WineApiDetailDto? Detail,
    Guid? SavedWineId);

public record IdentifyTextRequest(string Query);

public record WineApiIdentifyTestResult(
    Guid CorrelationId,
    bool Found,
    WineApiIdentifyResultDto? Result,
    Dictionary<string, Guid> SavedWines);
