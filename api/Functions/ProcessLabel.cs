using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace VinLoggen.Api.Functions;

// ── Request / Response DTOs ───────────────────────────────────────────────────

public record ProcessLabelRequest(string ImageUrl);

/// <summary>
/// Wine data extracted from the label by Gemini 2.0 Flash.
/// All fields are nullable — the model fills in what it can recognise.
/// </summary>
public record ProcessLabelResponse(
    string? Name,
    string? Producer,
    int? Vintage,
    string? Type,
    string? Country,
    string? Region,
    string? GrapeVariety,
    string? AlcoholContent
);

// ── Function ──────────────────────────────────────────────────────────────────

public class ProcessLabel(ILogger<ProcessLabel> logger, IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Function("ProcessLabel")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "process-label")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ProcessLabel triggered");

        var body = await req.ReadAsStringAsync() ?? string.Empty;
        var request = JsonSerializer.Deserialize<ProcessLabelRequest>(body, JsonOptions);

        if (string.IsNullOrWhiteSpace(request?.ImageUrl))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await bad.WriteStringAsync("""{"error":"imageUrl is required"}""");
            return bad;
        }

        logger.LogInformation("Processing wine label from: {ImageUrl}", request.ImageUrl);

        // ── TODO: Gemini 2.0 Flash integration ───────────────────────────────
        //
        // 1. Fetch the image bytes from Supabase Storage (request.ImageUrl)
        // 2. Send to Gemini via:
        //    POST https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent
        //    Authorization: Bearer {GEMINI_API_KEY}
        //
        //    Body (multimodal):
        //    {
        //      "contents": [{
        //        "parts": [
        //          { "text": "Extract from this wine label: name, producer, vintage year, wine type, country, region, grape variety, alcohol %. Respond in JSON." },
        //          { "inline_data": { "mime_type": "image/jpeg", "data": "<base64>" } }
        //        ]
        //      }]
        //    }
        //
        // 3. Parse the JSON response from Gemini into ProcessLabelResponse.
        // 4. Optionally enrich with Vinmonopolet API:
        //    GET https://www.vinmonopolet.no/api/products?query={name}
        // ─────────────────────────────────────────────────────────────────────

        var result = new ProcessLabelResponse(
            Name: null,
            Producer: null,
            Vintage: null,
            Type: null,
            Country: null,
            Region: null,
            GrapeVariety: null,
            AlcoholContent: null
        );

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(result, JsonOptions));
        return response;
    }
}
