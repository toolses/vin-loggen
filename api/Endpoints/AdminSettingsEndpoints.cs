using Microsoft.AspNetCore.Http.HttpResults;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Endpoints;

public static class AdminSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings")
            .WithTags("AdminSettings")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllSettings)
            .WithName("GetAllSettings")
            .WithSummary("Get all runtime app settings");

        group.MapPut("/{key}", UpdateSetting)
            .WithName("UpdateSetting")
            .WithSummary("Update a runtime app setting");

        return app;
    }

    private static async Task<Ok<Dictionary<string, string>>> GetAllSettings(
        IAppSettingsService settingsService,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAllAsync(ct);
        return TypedResults.Ok(settings);
    }

    private static async Task<Results<Ok, ProblemHttpResult>> UpdateSetting(
        string key,
        UpdateSettingRequest request,
        IAppSettingsService settingsService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return TypedResults.Problem(
                detail: "Value cannot be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await settingsService.SetAsync(key, request.Value, ct);
        return TypedResults.Ok();
    }
}

public record UpdateSettingRequest(string Value);
