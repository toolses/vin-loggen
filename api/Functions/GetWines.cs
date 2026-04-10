using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Net;
using System.Text.Json;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Functions;

public class GetWines(ILogger<GetWines> logger, NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Function("GetWines")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wines")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("GetWines triggered");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Aliases map snake_case columns → PascalCase record properties for Dapper.
        var wines = await connection.QueryAsync<WineRecord>(
            """
            SELECT
                id,
                name,
                producer,
                vintage,
                type,
                country,
                region,
                rating,
                notes,
                image_url   AS ImageUrl,
                created_at  AS CreatedAt
            FROM wines
            ORDER BY created_at DESC
            """
        );

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(wines, JsonOptions));
        return response;
    }
}
