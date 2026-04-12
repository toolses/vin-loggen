using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Endpoints;

public static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/me", CheckAdminStatus)
            .WithTags("Admin")
            .WithName("CheckAdminStatus")
            .WithSummary("Check if the authenticated user is an admin")
            .RequireAuthorization();

        return app;
    }

    private static Results<Ok<AdminStatusResponse>, ProblemHttpResult> CheckAdminStatus(
        ClaimsPrincipal user,
        AdminSettings adminSettings)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue("sub");

        if (sub is null || !Guid.TryParse(sub, out var uid))
        {
            return TypedResults.Problem(
                detail: "Could not resolve user identity from token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return TypedResults.Ok(new AdminStatusResponse(adminSettings.IsAdmin(uid)));
    }
}

public record AdminStatusResponse(bool IsAdmin);
