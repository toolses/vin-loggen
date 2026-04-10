namespace VinLoggen.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
            }))
            .WithTags("Health")
            .ExcludeFromDescription();

        return app;
    }
}
