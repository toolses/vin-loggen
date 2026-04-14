using System.Security.Claims;
using System.Text.Json;
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

        group.MapPost("/ask-stream", AskExpertStream)
            .WithName("AskExpertStream")
            .WithSummary("Ask the VinSomm AI expert with real-time progress events via SSE (charges 1 quota)");

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

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

    // ── Ask (streaming SSE) ─────────────────────────────────────────────────

    private static async Task AskExpertStream(
        ExpertRequest        request,
        ClaimsPrincipal      user,
        IExpertService       expertService,
        HttpContext          httpContext,
        ILogger<Program>     logger,
        CancellationToken    ct)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        if (!TryGetUserId(user, out var userId))
        {
            await WriteSseEvent(httpContext, "error", new { detail = "Could not resolve user identity from token." });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await WriteSseEvent(httpContext, "error", new { detail = "Spørsmål kan ikke være tomt." });
            return;
        }

        try
        {
            var result = await expertService.AskStreamAsync(userId, request, async status =>
            {
                await WriteSseEvent(httpContext, "status", new { message = status });
            }, ct);

            await WriteSseEvent(httpContext, "result", result);
        }
        catch (ExpertQuotaExceededException ex)
        {
            await WriteSseEvent(httpContext, "error", new
            {
                detail = ex.Message,
                errorCode = "QuotaExceeded",
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in AskExpertStream for user {UserId}", userId);
            await WriteSseEvent(httpContext, "error", new { detail = "Kunne ikke behandle forespørselen." });
        }
    }

    private static async Task WriteSseEvent(HttpContext httpContext, string eventName, object data)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        await httpContext.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", httpContext.RequestAborted);
        await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
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
