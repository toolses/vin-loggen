using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

// ── Internal Gemini types ─────────────────────────────────────────────────────

file record GeminiRequest(GeminiContent[] Contents);
file record GeminiContent(GeminiPart[] Parts);
file record GeminiPart(string? Text = null, GeminiInlineData? InlineData = null);
file record GeminiInlineData([property: JsonPropertyName("mime_type")] string MimeType, string Data);
file record GeminiResponse(GeminiCandidate[]? Candidates);
file record GeminiCandidate(GeminiContent? Content);

// Deserialized JSON written by Gemini inside its text response
file record WineExtraction(
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
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

    private const string ExtractionPrompt = """
        You are a wine label reader. Analyse the provided wine label image and extract the following fields.
        Return ONLY a valid JSON object with exactly these keys (use null for any field you cannot determine):

        {
          "name": string or null,
          "producer": string or null,
          "vintage": integer or null,
          "type": one of "Rød"|"Hvit"|"Rosé"|"Musserende"|"Oransje"|"Dessert" or null,
          "country": string or null,
          "region": string or null,
          "grapeVariety": string or null,
          "alcoholContent": string or null
        }

        Do not include markdown fences, comments, or any text outside of the JSON object.
        """;

    public static IEndpointRouteBuilder MapProcessLabelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/process-label", ProcessLabel)
            .WithName("ProcessLabel")
            .WithSummary("Extract wine data from a label image via Gemini 2.0 Flash")
            .WithTags("AI");

        return app;
    }

    private static async Task<Results<Ok<ProcessLabelResponse>, ProblemHttpResult>> ProcessLabel(
        ProcessLabelRequest  request,
        IHttpClientFactory   httpClientFactory,
        IConfiguration       configuration,
        ILogger<Program>     logger,
        CancellationToken    ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return TypedResults.Problem("imageUrl is required", statusCode: StatusCodes.Status400BadRequest);

        var apiKey = configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return TypedResults.Problem("Gemini API key not configured", statusCode: StatusCodes.Status503ServiceUnavailable);

        logger.LogInformation("ProcessLabel: fetching image {ImageUrl}", request.ImageUrl);

        var httpClient = httpClientFactory.CreateClient();

        // ── 1. Fetch image and encode to base64 ───────────────────────────────
        byte[] imageBytes;
        string mimeType;
        try
        {
            using var imageResponse = await httpClient.GetAsync(request.ImageUrl, ct);
            imageResponse.EnsureSuccessStatusCode();
            imageBytes = await imageResponse.Content.ReadAsByteArrayAsync(ct);
            mimeType   = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessLabel: failed to fetch image");
            return TypedResults.Problem("Could not fetch the label image", statusCode: StatusCodes.Status502BadGateway);
        }

        var base64Image = Convert.ToBase64String(imageBytes);

        // ── 2. Call Gemini 2.0 Flash ──────────────────────────────────────────
        var payload = new GeminiRequest(
            Contents:
            [
                new GeminiContent(Parts:
                [
                    new GeminiPart(Text: ExtractionPrompt),
                    new GeminiPart(InlineData: new GeminiInlineData(mimeType, base64Image)),
                ])
            ]
        );

        HttpResponseMessage geminiResponse;
        try
        {
            geminiResponse = await httpClient.PostAsJsonAsync(
                $"{GeminiEndpoint}?key={apiKey}", payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessLabel: Gemini request failed");
            return TypedResults.Problem("Gemini API request failed", statusCode: StatusCodes.Status502BadGateway);
        }

        if (!geminiResponse.IsSuccessStatusCode)
        {
            var body = await geminiResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("ProcessLabel: Gemini returned {Status}: {Body}", geminiResponse.StatusCode, body);
            return TypedResults.Problem(
                $"Gemini API error: {geminiResponse.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // ── 3. Parse Gemini response ──────────────────────────────────────────
        GeminiResponse? parsed;
        try
        {
            parsed = await geminiResponse.Content.ReadFromJsonAsync<GeminiResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessLabel: failed to parse Gemini response");
            return TypedResults.Problem("Failed to parse Gemini response", statusCode: StatusCodes.Status502BadGateway);
        }

        var rawText = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            logger.LogWarning("ProcessLabel: Gemini returned empty content");
            return TypedResults.Ok(new ProcessLabelResponse(null, null, null, null, null, null, null, null));
        }

        // Strip markdown fences if Gemini includes them despite instructions
        rawText = Regex.Replace(rawText.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        WineExtraction? extraction;
        try
        {
            extraction = JsonSerializer.Deserialize<WineExtraction>(
                rawText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "ProcessLabel: Gemini JSON could not be parsed. Raw: {Raw}", rawText);
            return TypedResults.Problem("Gemini returned malformed JSON", statusCode: StatusCodes.Status502BadGateway);
        }

        if (extraction is null)
            return TypedResults.Ok(new ProcessLabelResponse(null, null, null, null, null, null, null, null));

        logger.LogInformation("ProcessLabel: extracted {Name} {Vintage}", extraction.Name, extraction.Vintage);

        return TypedResults.Ok(new ProcessLabelResponse(
            extraction.Name,
            extraction.Producer,
            extraction.Vintage,
            extraction.Type,
            extraction.Country,
            extraction.Region,
            extraction.GrapeVariety,
            extraction.AlcoholContent
        ));
    }
}
