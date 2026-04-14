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
        CancellationToken ct)
    {
        var userId = ExtractUserId(user);
        var correlationId = Guid.NewGuid();

        var hits = await wineApiService.SearchRawAsync(
            producer, name, vintage, ct, userId, correlationId);

        return TypedResults.Ok(new WineApiSearchTestResult(
            CorrelationId: correlationId,
            HitCount: hits?.Count ?? 0,
            Hits: hits ?? []));
    }

    private static async Task<Results<Ok<WineApiDetailTestResult>, ProblemHttpResult>> GetWineApiDetails(
        string wineId,
        ClaimsPrincipal user,
        IWineApiService wineApiService,
        CancellationToken ct)
    {
        var userId = ExtractUserId(user);
        var correlationId = Guid.NewGuid();

        var detail = await wineApiService.GetDetailsRawAsync(
            wineId, ct, userId, correlationId);

        return TypedResults.Ok(new WineApiDetailTestResult(
            CorrelationId: correlationId,
            Found: detail is not null,
            Detail: detail));
    }

    private static async Task<Results<Ok<WineApiIdentifyTestResult>, ProblemHttpResult>> IdentifyByText(
        IdentifyTextRequest body,
        ClaimsPrincipal user,
        IWineApiService wineApiService,
        CancellationToken ct)
    {
        var userId = ExtractUserId(user);
        var correlationId = Guid.NewGuid();

        var result = await wineApiService.IdentifyByTextRawAsync(
            body.Query, ct, userId, correlationId);

        return TypedResults.Ok(new WineApiIdentifyTestResult(
            CorrelationId: correlationId,
            Found: result?.Wine is not null,
            Result: result));
    }
}

// ── Response wrapper records ─────────────────────────────────────────────

public record WineApiSearchTestResult(
    Guid CorrelationId,
    int HitCount,
    List<WineApiSearchHitDto> Hits);

public record WineApiDetailTestResult(
    Guid CorrelationId,
    bool Found,
    WineApiDetailDto? Detail);

public record IdentifyTextRequest(string Query);

public record WineApiIdentifyTestResult(
    Guid CorrelationId,
    bool Found,
    WineApiIdentifyResultDto? Result);
