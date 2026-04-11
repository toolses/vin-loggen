using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
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
            .WithSummary("Accept a label image (multipart), call Gemini, return structured wine data with deduplication info")
            .WithTags("AI")
            .DisableAntiforgery(); // Pure API endpoint — no browser form token needed

        return app;
    }

    // File-scoped helper – only used within this endpoint
    private record ExistingWineMatch(Guid WineId, int UserLogCount, decimal? LastRating, DateOnly? LastTastedAt);

    private static async Task<Results<Ok<WineAnalysisResponse>, ProblemHttpResult>> AnalyzeLabel(
        IFormFile        image,
        ClaimsPrincipal  user,
        GeminiService    geminiService,
        NpgsqlDataSource dataSource,
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

        if (!result.IsSuccess)
            return TypedResults.Problem(
                $"AI analysis failed: {result.Error}",
                statusCode: StatusCodes.Status502BadGateway);

        var analysis = result.Value!;

        // ── Deduplication check ───────────────────────────────────────────────
        // Requires both a recognisable wine identity and an authenticated user.
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.FindFirstValue("sub");

        if (!string.IsNullOrEmpty(analysis.WineName)
            && !string.IsNullOrEmpty(analysis.Producer)
            && Guid.TryParse(userIdClaim, out var userId))
        {
            try
            {
                await using var conn = await dataSource.OpenConnectionAsync(ct);

                // Look for a matching wine in the catalogue and count this
                // user's existing logs for it.
                var match = await conn.QueryFirstOrDefaultAsync<ExistingWineMatch>(
                    """
                    SELECT
                        w.id                                                    AS WineId,
                        COUNT(wl.id)                                            AS UserLogCount,
                        (SELECT rating    FROM wine_logs
                         WHERE wine_id = w.id AND user_id = @UserId
                         ORDER BY created_at DESC LIMIT 1)                     AS LastRating,
                        (SELECT tasted_at FROM wine_logs
                         WHERE wine_id = w.id AND user_id = @UserId
                         ORDER BY created_at DESC LIMIT 1)                     AS LastTastedAt
                    FROM wines w
                    LEFT JOIN wine_logs wl
                           ON wl.wine_id = w.id AND wl.user_id = @UserId
                    WHERE LOWER(TRIM(w.producer)) = LOWER(TRIM(@Producer))
                      AND LOWER(TRIM(w.name))     = LOWER(TRIM(@Name))
                      AND COALESCE(w.vintage, -1) = COALESCE(@Vintage::INT, -1)
                    GROUP BY w.id
                    LIMIT 1
                    """,
                    new
                    {
                        Producer = analysis.Producer,
                        Name     = analysis.WineName,
                        Vintage  = analysis.Vintage,
                        UserId   = userId,
                    });

                if (match is not null)
                {
                    logger.LogInformation(
                        "AnalyzeLabel: found existing wine {WineId} (user logs: {Count})",
                        match.WineId, match.UserLogCount);

                    return TypedResults.Ok(analysis with
                    {
                        AlreadyTasted  = match.UserLogCount > 0,
                        ExistingWineId = match.WineId,
                        LastRating     = match.LastRating,
                        LastTastedAt   = match.LastTastedAt,
                    });
                }
            }
            catch (NpgsqlException ex)
            {
                // Non-fatal: log and fall through, returning raw analysis without dedup info
                logger.LogWarning(ex, "AnalyzeLabel: deduplication DB check failed, skipping");
            }
        }

        return TypedResults.Ok(analysis);
    }
}
