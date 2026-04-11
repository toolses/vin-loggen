using System.Text.Json;
using Dapper;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Services;

public sealed class TasteProfileService
{
    private readonly IGeminiService _geminiService;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<TasteProfileService> _logger;

    public TasteProfileService(
        IGeminiService geminiService,
        NpgsqlDataSource dataSource,
        ILogger<TasteProfileService> logger)
    {
        _geminiService = geminiService;
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<TasteProfileResponse?> GetOrGenerateProfileAsync(
        Guid userId, bool forceRegenerate, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 1. Fetch existing profile
        var profile = await conn.QuerySingleOrDefaultAsync<UserProfile>(
            """
            SELECT
                user_id            AS UserId,
                taste_profile_json AS TasteProfileJson,
                wines_at_last_analysis AS WinesAtLastAnalysis,
                last_analysis_at   AS LastAnalysisAt,
                created_at         AS CreatedAt,
                updated_at         AS UpdatedAt
            FROM user_profiles
            WHERE user_id = @UserId
            """,
            new { UserId = userId });

        // 2. Count distinct wines the user has logged
        var currentWineCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT wine_id) FROM wine_logs WHERE user_id = @UserId",
            new { UserId = userId });

        // 3. Check cache validity
        var needsRegeneration = forceRegenerate
            || profile is null
            || profile.TasteProfileJson is null
            || (currentWineCount - profile.WinesAtLastAnalysis) > 5
            || profile.LastAnalysisAt is null
            || (DateTime.UtcNow - profile.LastAnalysisAt.Value).TotalDays > 7;

        // 4. Return cached if still valid
        if (!needsRegeneration && profile?.TasteProfileJson is not null)
        {
            _logger.LogInformation("TasteProfile: returning cached profile for user {UserId}", userId);
            return JsonSerializer.Deserialize<TasteProfileResponse>(
                profile.TasteProfileJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        // 5. Need at least 1 wine to generate a profile
        if (currentWineCount == 0)
        {
            _logger.LogInformation("TasteProfile: no wines for user {UserId}, skipping generation", userId);
            return null;
        }

        // 6. Fetch user's wines joined with their most-recent log (for rating)
        var wines = await conn.QueryAsync<WineProfileData>(
            """
            SELECT DISTINCT ON (wl.wine_id)
                w.name       AS Name,
                w.producer   AS Producer,
                w.vintage    AS Vintage,
                w.type       AS Type,
                w.country    AS Country,
                w.region     AS Region,
                wl.rating    AS Rating
            FROM wine_logs wl
            JOIN wines w ON w.id = wl.wine_id
            WHERE wl.user_id = @UserId
            ORDER BY wl.wine_id, wl.created_at DESC
            """,
            new { UserId = userId });

        // 7. Call Gemini
        _logger.LogInformation(
            "TasteProfile: generating new profile for user {UserId} ({WineCount} wines)",
            userId, currentWineCount);

        var result = await _geminiService.GenerateTasteProfileAsync(wines, ct);
        if (result is null)
        {
            _logger.LogWarning("TasteProfile: Gemini returned null for user {UserId}", userId);
            return null;
        }

        // 8. Upsert into user_profiles
        var profileJson = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        await conn.ExecuteAsync(
            """
            INSERT INTO user_profiles (user_id, taste_profile_json, wines_at_last_analysis, last_analysis_at, updated_at)
            VALUES (@UserId, @ProfileJson::jsonb, @WineCount, NOW(), NOW())
            ON CONFLICT (user_id) DO UPDATE SET
                taste_profile_json     = @ProfileJson::jsonb,
                wines_at_last_analysis = @WineCount,
                last_analysis_at       = NOW(),
                updated_at             = NOW()
            """,
            new { UserId = userId, ProfileJson = profileJson, WineCount = currentWineCount });

        return result;
    }
}
