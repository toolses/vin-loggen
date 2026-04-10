using Microsoft.AspNetCore.Http.HttpResults;
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
            .WithSummary("Accept a label image (multipart), call Gemini 2.0 Flash, return structured wine data")
            .WithTags("AI")
            .DisableAntiforgery(); // Pure API endpoint — no browser form token needed

        return app;
    }

    private static async Task<Results<Ok<WineAnalysisResponse>, ProblemHttpResult>> AnalyzeLabel(
        IFormFile        image,
        GeminiService    geminiService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return TypedResults.Problem(
                "No image file provided", statusCode: StatusCodes.Status400BadRequest);

        if (image.Length > MaxImageBytes)
            return TypedResults.Problem(
                "Image too large (max 10 MB)", statusCode: StatusCodes.Status400BadRequest);

        if (!AllowedMimeTypes.Contains(image.ContentType, StringComparer.OrdinalIgnoreCase))
            return TypedResults.Problem(
                "Unsupported image type. Use JPEG, PNG, or WebP.",
                statusCode: StatusCodes.Status400BadRequest);

        logger.LogInformation(
            "AnalyzeLabel: {FileName} ({ContentType}, {Bytes} bytes)",
            image.FileName, image.ContentType, image.Length);

        byte[] imageBytes;
        using (var ms = new MemoryStream((int)image.Length))
        {
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
        }

        var result = await geminiService.AnalyzeLabelAsync(imageBytes, image.ContentType, ct);

        if (result is null)
            return TypedResults.Problem(
                "AI analysis failed – could not extract wine data from the image",
                statusCode: StatusCodes.Status502BadGateway);

        return TypedResults.Ok(result);
    }
}
