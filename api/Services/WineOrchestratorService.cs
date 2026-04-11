using Dapper;
using Npgsql;
using VinLoggen.Api.Configuration;

namespace VinLoggen.Api.Services;

/// <summary>
/// Tier-aware analysis pipeline.
///
/// Step 1 – Basic (always):
///   Gemini OCR → structured wine identity data
///
/// Step 2 – Deduplication (when authenticated):
///   Check wines + wine_logs for a prior entry by the same user
///
/// Step 3 – Pro enrichment (when authenticated + quota available):
///   a. wineapi.io lookup → description, food pairings, technical notes
///   b. If wineapi.io returns no food pairings → fallback Gemini Pro prompt
///
/// Quota is only charged when step 3 produces a meaningful enrichment result.
/// Technical API failures in step 3 are swallowed so the user always receives
/// at least the basic OCR result.
/// </summary>
public sealed class WineOrchestratorService
{
    private readonly IGeminiService      _gemini;
    private readonly IWineApiService     _wineApi;
    private readonly IProUsageService    _proUsage;
    private readonly NpgsqlDataSource    _dataSource;
    private readonly IntegrationSettings _settings;
    private readonly ILogger<WineOrchestratorService> _logger;

    // Internal Dapper projection for the dedup query
    private record DedupMatch(Guid WineId, int UserLogCount, decimal? LastRating, DateOnly? LastTastedAt);

    public WineOrchestratorService(
        IGeminiService                      gemini,
        IWineApiService                     wineApi,
        IProUsageService                    proUsage,
        NpgsqlDataSource                    dataSource,
        IntegrationSettings                 settings,
        ILogger<WineOrchestratorService>    logger)
    {
        _gemini     = gemini;
        _wineApi    = wineApi;
        _proUsage   = proUsage;
        _dataSource = dataSource;
        _settings   = settings;
        _logger     = logger;
    }

    /// <summary>
    /// Runs the full analysis pipeline for the supplied image bytes.
    /// <paramref name="userId"/> may be <c>null</c> for unauthenticated calls
    /// (dedup + quota features are skipped).
    /// </summary>
    public async Task<WineAnalysisResponse> AnalyzeAsync(
        byte[]  imageBytes,
        string  mimeType,
        Guid?   userId,
        CancellationToken ct)
    {
        // ── Step 1: Gemini OCR ────────────────────────────────────────────────
        if (!_settings.EnableGemini)
        {
            _logger.LogWarning("OrchestratorService: Gemini disabled, returning empty result");
            return new WineAnalysisResponse(null, null, null, null, null, null, null, null);
        }

        var geminiResult = await _gemini.AnalyzeLabelAsync(imageBytes, mimeType, ct);
        if (!geminiResult.IsSuccess)
        {
            _logger.LogError("OrchestratorService: Gemini OCR failed: {Error}", geminiResult.Error);
            throw new InvalidOperationException($"AI analysis failed: {geminiResult.Error}");
        }

        var analysis = geminiResult.Value!;

        // Label was unreadable (Gemini returned all nulls) – no charge
        bool labelReadable = !string.IsNullOrWhiteSpace(analysis.WineName);

        // ── Step 2: Deduplication (authenticated only) ────────────────────────
        DedupMatch? dedup = null;
        if (userId.HasValue && labelReadable && !string.IsNullOrWhiteSpace(analysis.Producer))
        {
            dedup = await CheckDuplicateAsync(analysis.Producer, analysis.WineName!, analysis.Vintage, userId.Value, ct);
        }

        // ── Step 3: Pro quota check ───────────────────────────────────────────
        ProUsageService.ProStatus? proStatus = null;
        if (userId.HasValue && labelReadable && _settings.EnableWineApi)
        {
            proStatus = await _proUsage.GetStatusAsync(userId.Value, ct);
        }

        // ── Step 4: Enrichment (Pro quota available) ──────────────────────────
        WineApiService.WineEnrichment? enrichment = null;
        bool quotaCharged = false;

        if (proStatus?.CanUsePro == true && labelReadable)
        {
            enrichment = await _wineApi.FindAsync(
                analysis.Producer ?? "",
                analysis.WineName!,
                analysis.Vintage,
                ct);

            // Fallback: ask Gemini for food pairings if wineapi.io had none
            if (enrichment?.FoodPairings is not { Length: > 0 })
            {
                var geminiFoodResult = await _gemini.GetFoodPairingsAsync(
                    analysis.WineName, analysis.Producer,
                    analysis.Vintage, analysis.Type, analysis.Country, ct);

                if (geminiFoodResult is not null)
                {
                    // Merge Gemini pairings into (potentially null) enrichment
                    enrichment = enrichment is not null
                        ? enrichment with
                        {
                            FoodPairings   = geminiFoodResult.FoodPairings,
                            TechnicalNotes = enrichment.TechnicalNotes ?? geminiFoodResult.TechnicalNotes,
                        }
                        : new WineApiService.WineEnrichment(
                            ExternalId:    null,
                            Description:   null,
                            FoodPairings:  geminiFoodResult.FoodPairings,
                            TechnicalNotes: geminiFoodResult.TechnicalNotes,
                            AlcoholContent: null,
                            Grapes:        null);
                }
            }

            // Charge quota only when the Pro step produced something useful
            if (userId.HasValue && enrichment?.FoodPairings is { Length: > 0 })
            {
                await _proUsage.IncrementAsync(userId.Value, ct);
                quotaCharged = true;
                // Refresh the status so the response reflects the new counter
                proStatus = await _proUsage.GetStatusAsync(userId.Value, ct);

                _logger.LogInformation(
                    "OrchestratorService: Pro scan charged for user {UserId} ({Scans}/{Limit})",
                    userId, proStatus.ScansToday, proStatus.DailyLimit);
            }
        }

        _ = quotaCharged; // explicit use to suppress warning

        // ── Step 5: Assemble response ─────────────────────────────────────────
        bool proLimitReached = proStatus is { CanUsePro: false };

        return analysis with
        {
            // Dedup
            AlreadyTasted  = dedup is not null && dedup.UserLogCount > 0,
            ExistingWineId = dedup?.WineId,
            LastRating     = dedup?.LastRating,
            LastTastedAt   = dedup?.LastTastedAt,
            // Pro enrichment
            FoodPairings    = enrichment?.FoodPairings,
            Description     = enrichment?.Description,
            TechnicalNotes  = enrichment?.TechnicalNotes,
            ExternalSourceId = enrichment?.ExternalId
                              ?? (dedup is not null
                                  ? await GetExistingExternalIdAsync(dedup.WineId, ct)
                                  : null),
            // Quota
            ProLimitReached = proLimitReached,
            ProScansToday   = proStatus?.ScansToday   ?? 0,
            DailyProLimit   = proStatus?.DailyLimit   ?? _settings.DailyProLimit,
            IsPro           = proStatus?.IsPro        ?? false,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<DedupMatch?> CheckDuplicateAsync(
        string producer, string name, int? vintage, Guid userId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<DedupMatch>(
                """
                SELECT
                    w.id AS WineId,
                    (SELECT COUNT(*) FROM wine_logs WHERE wine_id = w.id AND user_id = @UserId) AS UserLogCount,
                    (SELECT rating    FROM wine_logs WHERE wine_id = w.id AND user_id = @UserId ORDER BY created_at DESC LIMIT 1) AS LastRating,
                    (SELECT tasted_at FROM wine_logs WHERE wine_id = w.id AND user_id = @UserId ORDER BY created_at DESC LIMIT 1) AS LastTastedAt
                FROM wines w
                WHERE LOWER(TRIM(w.producer)) = LOWER(TRIM(@Producer))
                  AND LOWER(TRIM(w.name))     = LOWER(TRIM(@Name))
                  AND COALESCE(w.vintage, -1) = COALESCE(@Vintage::INT, -1)
                LIMIT 1
                """,
                new { Producer = producer, Name = name, Vintage = vintage, UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OrchestratorService: dedup DB check failed, skipping");
            return null;
        }
    }

    private async Task<string?> GetExistingExternalIdAsync(Guid wineId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            return await conn.ExecuteScalarAsync<string?>(
                "SELECT external_source_id FROM wines WHERE id = @WineId",
                new { WineId = wineId });
        }
        catch
        {
            return null;
        }
    }
}
