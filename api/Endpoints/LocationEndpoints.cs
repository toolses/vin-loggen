using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Endpoints;

public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/locations")
            .WithTags("Locations")
            .RequireAuthorization();

        group.MapGet("/autocomplete", Autocomplete)
            .WithName("LocationAutocomplete")
            .WithSummary("Search for places via Google Places Autocomplete");

        group.MapGet("/details", GetDetails)
            .WithName("LocationDetails")
            .WithSummary("Get place details (name, address, coordinates) by Google Place ID");

        return app;
    }

    private static async Task<Results<Ok<List<PlaceAutocompleteSuggestion>>, ProblemHttpResult>> Autocomplete(
        string            query,
        string            sessionToken,
        double?           lat,
        double?           lng,
        IGooglePlacesService placesService,
        ILogger<Program>  logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return TypedResults.Problem(
                detail: "Query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return TypedResults.Problem(
                detail: "Session token is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var results = await placesService.AutocompleteAsync(query, sessionToken, lat, lng, ct);
            return TypedResults.Ok(results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "LocationEndpoints: autocomplete failed");
            return TypedResults.Problem(
                detail: "Location search temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<Results<Ok<PlaceDetailsResult>, ProblemHttpResult>> GetDetails(
        string            placeId,
        string            sessionToken,
        IGooglePlacesService placesService,
        ILogger<Program>  logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return TypedResults.Problem(
                detail: "placeId parameter is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return TypedResults.Problem(
                detail: "Session token is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await placesService.GetDetailsAsync(placeId, sessionToken, ct);
            if (result is null)
            {
                return TypedResults.Problem(
                    detail: "Place not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return TypedResults.Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "LocationEndpoints: details failed for '{PlaceId}'", placeId);
            return TypedResults.Problem(
                detail: "Location details temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
