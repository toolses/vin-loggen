using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllUsers)
            .WithName("GetAllUsers")
            .WithSummary("List all user profiles for admin management");

        group.MapPatch("/{id:guid}/tier", UpdateUserTier)
            .WithName("UpdateUserTier")
            .WithSummary("Change a user's subscription tier (free/pro)");

        return app;
    }

    private static async Task<Results<Ok<IEnumerable<AdminUserListItem>>, ProblemHttpResult>> GetAllUsers(
        string? search,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        const string sql = """
            SELECT
                up.user_id                              AS Id,
                au.email                                AS Email,
                COALESCE(au.raw_user_meta_data->>'display_name',
                         au.raw_user_meta_data->>'full_name',
                         au.raw_user_meta_data->>'name') AS DisplayName,
                up.subscription_tier                    AS SubscriptionTier,
                CASE WHEN up.last_pro_scan_date = CURRENT_DATE
                     THEN up.pro_scans_today ELSE 0
                END                                     AS ProScansToday,
                up.is_admin                             AS IsAdmin,
                up.created_at                           AS CreatedAt
            FROM user_profiles up
            JOIN auth.users au ON au.id = up.user_id
            WHERE (@Search IS NULL
                   OR au.email                                     ILIKE '%' || @Search || '%'
                   OR au.raw_user_meta_data->>'display_name'       ILIKE '%' || @Search || '%'
                   OR au.raw_user_meta_data->>'full_name'          ILIKE '%' || @Search || '%'
                   OR au.raw_user_meta_data->>'name'               ILIKE '%' || @Search || '%')
            ORDER BY up.created_at DESC
            """;

        var rows = await conn.QueryAsync<AdminUserListItem>(sql, new { Search = search });
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<AdminUserListItem>, ProblemHttpResult>> UpdateUserTier(
        Guid id,
        AdminUserTierUpdateRequest request,
        NpgsqlDataSource dataSource,
        CancellationToken ct)
    {
        var tier = request.SubscriptionTier?.ToLowerInvariant();
        if (tier is not ("free" or "pro"))
            return TypedResults.Problem(detail: "subscription_tier must be 'free' or 'pro'.", statusCode: 400);

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var affected = await conn.ExecuteAsync(
            """
            UPDATE user_profiles
            SET subscription_tier = @Tier, updated_at = NOW()
            WHERE user_id = @Id
            """,
            new { Tier = tier, Id = id });

        if (affected == 0)
            return TypedResults.Problem(detail: "User not found.", statusCode: 404);

        var updated = await conn.QuerySingleAsync<AdminUserListItem>(
            """
            SELECT
                up.user_id                              AS Id,
                au.email                                AS Email,
                COALESCE(au.raw_user_meta_data->>'display_name',
                         au.raw_user_meta_data->>'full_name',
                         au.raw_user_meta_data->>'name') AS DisplayName,
                up.subscription_tier                    AS SubscriptionTier,
                CASE WHEN up.last_pro_scan_date = CURRENT_DATE
                     THEN up.pro_scans_today ELSE 0
                END                                     AS ProScansToday,
                up.is_admin                             AS IsAdmin,
                up.created_at                           AS CreatedAt
            FROM user_profiles up
            JOIN auth.users au ON au.id = up.user_id
            WHERE up.user_id = @Id
            """,
            new { Id = id });

        return TypedResults.Ok(updated);
    }
}
