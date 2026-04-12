using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Endpoints;

public static class TasteProfileEndpoints
{
    public static IEndpointRouteBuilder MapTasteProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        group.MapGet("/taste", GetTasteProfile)
            .WithName("GetTasteProfile")
            .WithSummary("Get the user's AI taste profile (cached, regenerates if stale)");

        group.MapPost("/taste/regenerate", RegenerateTasteProfile)
            .WithName("RegenerateTasteProfile")
            .WithSummary("Force regeneration of the AI taste profile");

        return app;
    }

    private static async Task<Results<Ok<TasteProfileResponse>, ProblemHttpResult>> GetTasteProfile(
        ClaimsPrincipal user,
        TasteProfileService profileService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!TryGetUserId(user, out var userId))
        {
            return TypedResults.Problem(
                detail: "Could not resolve user identity from token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            var result = await profileService.GetOrGenerateProfileAsync(userId, forceRegenerate: false, ct);
            if (result is null)
            {
                return TypedResults.Problem(
                    detail: "Ikke nok data for å generere smaksprofil. Legg til flere viner først.",
                    statusCode: StatusCodes.Status404NotFound);
            }
            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting taste profile for user {UserId}", userId);
            return TypedResults.Problem(
                detail: "Kunne ikke hente smaksprofil.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<Results<Ok<TasteProfileResponse>, ProblemHttpResult>> RegenerateTasteProfile(
        ClaimsPrincipal user,
        TasteProfileService profileService,
        IProUsageService proUsage,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!TryGetUserId(user, out var userId))
        {
            return TypedResults.Problem(
                detail: "Could not resolve user identity from token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Check daily AI quota before calling Gemini
        var proStatus = await proUsage.GetStatusAsync(userId, ct);
        if (!proStatus.CanUsePro)
        {
            return TypedResults.Problem(
                detail: "Daglig kvote for AI-forespørsler er nådd. Prøv igjen i morgen.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var result = await profileService.GetOrGenerateProfileAsync(userId, forceRegenerate: true, ct);
            if (result is null)
            {
                return TypedResults.Problem(
                    detail: "Ikke nok data for å generere smaksprofil. Legg til flere viner først.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // Charge one AI request for the Gemini call
            await proUsage.IncrementAsync(userId, ct);

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error regenerating taste profile for user {UserId}", userId);
            return TypedResults.Problem(
                detail: "Kunne ikke regenerere smaksprofil.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

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
