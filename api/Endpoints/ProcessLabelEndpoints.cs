using Microsoft.AspNetCore.Http.HttpResults;

namespace VinLoggen.Api.Endpoints;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record ProcessLabelRequest(string ImageUrl);

/// <summary>
/// Extracted wine data returned to the Angular client.
/// All fields are nullable — the model fills in what it can recognise.
/// </summary>
public record ProcessLabelResponse(
    string? Name,
    string? Producer,
    int?    Vintage,
    string? Type,
    string? Country,
    string? Region,
    string? GrapeVariety,
    string? AlcoholContent
);

// ── Endpoint ──────────────────────────────────────────────────────────────────

public static class ProcessLabelEndpoints
{
    public static IEndpointRouteBuilder MapProcessLabelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/process-label", ProcessLabel)
            .WithName("ProcessLabel")
            .WithSummary("Extract wine data from a label image via Gemini 2.0 Flash")
            .WithTags("AI");

        return app;
    }

    private static async Task<Results<Ok<ProcessLabelResponse>, BadRequest<string>>> ProcessLabel(
        ProcessLabelRequest  request,
        IHttpClientFactory   httpClientFactory,
        IConfiguration       configuration,
        ILogger<Program>     logger,
        CancellationToken    ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return TypedResults.BadRequest("imageUrl is required");

        logger.LogInformation("ProcessLabel called for: {ImageUrl}", request.ImageUrl);

        // ── TODO: Gemini 2.0 Flash integration ───────────────────────────────
        //
        // 1. Fetch the image from Supabase Storage or accept base64 directly.
        //
        // 2. Call Gemini:
        //    var apiKey = configuration["GEMINI_API_KEY"];
        //    var client = httpClientFactory.CreateClient();
        //    var payload = new {
        //        contents = new[] { new {
        //            parts = new object[] {
        //                new { text = @"Extract from this wine label and return JSON only:
        //                    name, producer, vintage (int), type, country, region,
        //                    grapeVariety, alcoholContent." },
        //                new { inline_data = new {
        //                    mime_type = "image/jpeg",
        //                    data = "<base64-image>"
        //                }}
        //            }
        //        }}
        //    };
        //    var res = await client.PostAsJsonAsync(
        //        $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}",
        //        payload, ct);
        //
        // 3. Enrich via Vinmonopolet:
        //    GET https://www.vinmonopolet.no/api/products?query={name}&searchType=product
        // ─────────────────────────────────────────────────────────────────────

        await Task.CompletedTask; // remove when real implementation is added
        return TypedResults.Ok(new ProcessLabelResponse(null, null, null, null, null, null, null, null));
    }
}
