using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Models;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Endpoints;

public static class WineAnalyzeEndpoints
{
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp"];

    private const long MaxImageBytes = 10 * 1024 * 1024; // 10 MB

    public static IEndpointRouteBuilder MapWineAnalyzeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/wine/analyze", AnalyzeLabel)
            .WithName("AnalyzeWineLabel")
            .WithSummary("Analyze a wine label image; returns structured data + Pro enrichment when quota allows")
            .WithTags("AI")
            .DisableAntiforgery();

        return app;
    }

    private static async Task<Results<Ok<WineAnalysisResponse>, ProblemHttpResult>> AnalyzeLabel(
        IFormFile                 image,
        ClaimsPrincipal           user,
        WineOrchestratorService   orchestrator,
        ILogger<Program>          logger,
        CancellationToken         ct)
    {
        // ── Validate input ────────────────────────────────────────────────────
        if (image is null || image.Length == 0)
            return TypedResults.Problem("No image file provided", statusCode: 400);

        if (image.Length > MaxImageBytes)
            return TypedResults.Problem("Image too large (max 10 MB)", statusCode: 400);

        if (!AllowedMimeTypes.Contains(image.ContentType, StringComparer.OrdinalIgnoreCase))
            return TypedResults.Problem(
                "Unsupported image type. Use JPEG, PNG, or WebP.", statusCode: 400);

        logger.LogInformation(
            "AnalyzeLabel: {FileName} ({ContentType}, {Bytes} bytes)",
            image.FileName, image.ContentType, image.Length);

        // ── Read image bytes ──────────────────────────────────────────────────
        byte[] imageBytes;
        using (var ms = new MemoryStream((int)image.Length))
        {
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
        }

        // ── Resolve optional user identity ────────────────────────────────────
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.FindFirstValue("sub");
        Guid? userId = Guid.TryParse(userIdClaim, out var id) ? id : null;

        // ── Delegate to the orchestrator ──────────────────────────────────────
        var apiResult = await orchestrator.AnalyzeAsync(imageBytes, image.ContentType, userId, ct);

        if (apiResult.Success)
            return TypedResults.Ok(apiResult.Data!);

        var statusCode = apiResult.ErrorCode switch
        {
            ApiErrorCode.ImageUnreadable     => StatusCodes.Status422UnprocessableEntity,
            ApiErrorCode.ExternalServiceDown => StatusCodes.Status502BadGateway,
            ApiErrorCode.QuotaExceeded       => StatusCodes.Status429TooManyRequests,
            ApiErrorCode.Unauthorized        => StatusCodes.Status401Unauthorized,
            _                                => StatusCodes.Status500InternalServerError,
        };

        return TypedResults.Problem(
            detail: apiResult.Message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = apiResult.ErrorCode.ToString(),
            });
    }
}
