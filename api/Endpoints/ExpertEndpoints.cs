using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Models;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Endpoints;

public static class ExpertEndpoints
{
    public static IEndpointRouteBuilder MapExpertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expert")
            .WithTags("Expert")
            .RequireAuthorization();

        group.MapPost("/ask", AskExpert)
            .WithName("AskExpert")
            .WithSummary("Ask the VinSomm AI expert a wine-related question (charges 1 quota)");

        group.MapGet("/sessions", GetSessions)
            .WithName("GetExpertSessions")
            .WithSummary("List the user's past expert conversation sessions");

        group.MapGet("/sessions/{sessionId:guid}", GetSession)
            .WithName("GetExpertSession")
            .WithSummary("Get a full expert conversation session with messages and wine suggestions");

        group.MapDelete("/sessions/{sessionId:guid}", DeleteSession)
            .WithName("DeleteExpertSession")
            .WithSummary("Delete an expert conversation session");

        group.MapPatch("/suggestions/{suggestionId:guid}/feedback", SetFeedback)
            .WithName("SetWineSuggestionFeedback")
            .WithSummary("Submit thumbs-up or thumbs-down feedback on a wine suggestion");

        return app;
    }

    // ── Ask ──────────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<ExpertResponse>, ProblemHttpResult>> AskExpert(
        ExpertRequest        request,
        ClaimsPrincipal      user,
        IExpertService       expertService,
        ILogger<Program>     logger,
        CancellationToken    ct)
    {
        if (!TryGetUserId(user, out var userId))
        {
            return TypedResults.Problem(
                detail: "Could not resolve user identity from token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return TypedResults.Problem(
                detail: "Spørsmål kan ikke være tomt.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await expertService.AskAsync(userId, request, ct);
            return TypedResults.Ok(result);
        }
        catch (ExpertQuotaExceededException ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status429TooManyRequests,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "QuotaExceeded" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in AskExpert for user {UserId}", userId);
            return TypedResults.Problem(
                detail: "Kunne ikke behandle forespørselen.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ── Sessions ─────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<IEnumerable<ExpertSessionSummary>>, ProblemHttpResult>> GetSessions(
        ClaimsPrincipal    user,
        IExpertService     expertService,
        CancellationToken  ct,
        int                limit  = 20,
        int                offset = 0)
    {
        if (!TryGetUserId(user, out var userId))
            return TypedResults.Problem(detail: "Could not resolve user identity.", statusCode: 401);

        var sessions = await expertService.GetSessionsAsync(userId, Math.Clamp(limit, 1, 50), Math.Max(offset, 0), ct);
        return TypedResults.Ok(sessions);
    }

    private static async Task<Results<Ok<ExpertSessionDetail>, NotFound, ProblemHttpResult>> GetSession(
        Guid               sessionId,
        ClaimsPrincipal    user,
        IExpertService     expertService,
        CancellationToken  ct)
    {
        if (!TryGetUserId(user, out var userId))
            return TypedResults.Problem(detail: "Could not resolve user identity.", statusCode: 401);

        var session = await expertService.GetSessionAsync(userId, sessionId, ct);
        if (session is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(session);
    }

    private static async Task<Results<Ok, NotFound, ProblemHttpResult>> DeleteSession(
        Guid               sessionId,
        ClaimsPrincipal    user,
        IExpertService     expertService,
        CancellationToken  ct)
    {
        if (!TryGetUserId(user, out var userId))
            return TypedResults.Problem(detail: "Could not resolve user identity.", statusCode: 401);

        var deleted = await expertService.DeleteSessionAsync(userId, sessionId, ct);
        return deleted ? TypedResults.Ok() : TypedResults.NotFound();
    }

    // ── Feedback ─────────────────────────────────────────────────────────────

    private static async Task<Results<Ok, NotFound, ProblemHttpResult>> SetFeedback(
        Guid                             suggestionId,
        WineSuggestionFeedbackRequest    request,
        ClaimsPrincipal                  user,
        IExpertService                   expertService,
        CancellationToken                ct)
    {
        if (!TryGetUserId(user, out var userId))
            return TypedResults.Problem(detail: "Could not resolve user identity.", statusCode: 401);

        if (request.Feedback is not (1 or -1))
            return TypedResults.Problem(detail: "Feedback must be 1 or -1.", statusCode: 400);

        var updated = await expertService.SetWineFeedbackAsync(userId, suggestionId, request.Feedback, ct);
        return updated ? TypedResults.Ok() : TypedResults.NotFound();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var claim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub");

        if (claim is not null && Guid.TryParse(claim, out userId))
            return true;

        userId = Guid.Empty;
        return false;
    }
}
