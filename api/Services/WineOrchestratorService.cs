using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using VinLoggen.Api.Configuration;
using VinLoggen.Api.Models;
using VinLoggen.Api.Services.AiProviders;

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
///   b. If wineapi.io returns no food pairings → fallback via AI provider chain (DeepSeek → Gemini)
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
    private readonly AiProviderChain     _aiChain;
    private readonly NpgsqlDataSource    _dataSource;
    private readonly IntegrationSettings _settings;
    private readonly ILogger<WineOrchestratorService> _logger;

    // Internal Dapper projection for the dedup query.
    // Types must match what Npgsql/Dapper returns: COUNT(*)→long, date→DateTime.
    private record DedupMatch(Guid WineId, long UserLogCount, decimal? LastRating, DateTime? LastTastedAt);

    public WineOrchestratorService(
        IGeminiService                      gemini,
        IWineApiService                     wineApi,
        IProUsageService                    proUsage,
        AiProviderChain                     aiChain,
        NpgsqlDataSource                    dataSource,
        IntegrationSettings                 settings,
        ILogger<WineOrchestratorService>    logger)
    {
        _gemini     = gemini;
        _wineApi    = wineApi;
        _proUsage   = proUsage;
        _aiChain    = aiChain;
        _dataSource = dataSource;
        _settings   = settings;
        _logger     = logger;
    }

    /// <summary>
    /// Runs the full analysis pipeline for the supplied image bytes.
    /// <paramref name="userId"/> may be <c>null</c> for unauthenticated calls
    /// (dedup + quota features are skipped).
    /// <paramref name="backImageBytes"/> and <paramref name="backMimeType"/> are optional
    /// (omit when only a front label image is available).
    /// </summary>
    public async Task<ApiResult<WineAnalysisResponse>> AnalyzeAsync(
        byte[]  imageBytes,
        string  mimeType,
        Guid?   userId,
        CancellationToken ct,
        byte[]? backImageBytes = null,
        string? backMimeType   = null)
    {
        // ── Step 1: Gemini OCR ────────────────────────────────────────────────
        if (!_settings.EnableGemini)
        {
            _logger.LogWarning("OrchestratorService: Gemini disabled, returning empty result");
            return ApiResult<WineAnalysisResponse>.Fail(
                ApiErrorCode.ExternalServiceDown, "AI-analyse er deaktivert");
        }

        var geminiResult = backImageBytes is { Length: > 0 }
            ? await _gemini.AnalyzeLabelsAsync(imageBytes, mimeType, backImageBytes, backMimeType, ct)
            : await _gemini.AnalyzeLabelAsync(imageBytes, mimeType, ct);
        if (!geminiResult.IsSuccess)
        {
            _logger.LogError("OrchestratorService: Gemini OCR failed: {Error}", geminiResult.Error);

            var isServiceError = geminiResult.Error?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true
                              || geminiResult.Error?.Contains("HTTP request", StringComparison.OrdinalIgnoreCase) == true;

            return isServiceError
                ? ApiResult<WineAnalysisResponse>.Fail(
                    ApiErrorCode.ExternalServiceDown, "AI-tjenesten er midlertidig utilgjengelig")
                : ApiResult<WineAnalysisResponse>.Fail(
                    ApiErrorCode.ImageUnreadable, "Kunne ikke lese etiketten fra bildet");
        }

        var analysis = geminiResult.Value!;

        // Label was unreadable (Gemini returned all nulls) – no charge
        bool labelReadable = !string.IsNullOrWhiteSpace(analysis.WineName);

        if (!labelReadable)
        {
            return ApiResult<WineAnalysisResponse>.Fail(
                ApiErrorCode.ImageUnreadable, "Kunne ikke lese etiketten. Prøv et nytt bilde med bedre lys.");
        }

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

            // Fallback: ask AI provider chain (DeepSeek → Gemini) for enrichment if wineapi.io had gaps
            if (enrichment?.FoodPairings is not { Length: > 0 } || string.IsNullOrWhiteSpace(enrichment?.Description))
            {
                var aiFoodResult = await GetFoodPairingsViaChainAsync(
                    analysis.WineName, analysis.Producer,
                    analysis.Vintage, analysis.Type, analysis.Country, ct);

                if (aiFoodResult is not null)
                {
                    // Merge AI results into (potentially null) enrichment
                    enrichment = enrichment is not null
                        ? enrichment with
                        {
                            FoodPairings   = enrichment.FoodPairings is { Length: > 0 } ? enrichment.FoodPairings : aiFoodResult.FoodPairings,
                            TechnicalNotes = enrichment.TechnicalNotes ?? aiFoodResult.TechnicalNotes,
                            Description    = enrichment.Description ?? aiFoodResult.Description,
                        }
                        : new WineApiService.WineEnrichment(
                            ExternalId:    null,
                            Description:   aiFoodResult.Description,
                            FoodPairings:  aiFoodResult.FoodPairings,
                            TechnicalNotes: aiFoodResult.TechnicalNotes,
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

        return ApiResult<WineAnalysisResponse>.Ok(analysis with
        {
            // Dedup
            AlreadyTasted  = dedup is not null && dedup.UserLogCount > 0,
            ExistingWineId = dedup?.WineId,
            LastRating     = dedup?.LastRating,
            LastTastedAt   = dedup?.LastTastedAt is DateTime dt ? DateOnly.FromDateTime(dt) : null,
            // Pro enrichment
            FoodPairings    = enrichment?.FoodPairings,
            Description     = enrichment?.Description,
            TechnicalNotes  = enrichment?.TechnicalNotes,
            ExternalSourceId = enrichment?.ExternalId
                              ?? (dedup is not null
                                  ? await GetExistingExternalIdAsync(dedup.WineId, ct)
                                  : null),
            // Name suggestions from catalogue match
            SuggestedName     = enrichment?.SuggestedName,
            SuggestedProducer = enrichment?.SuggestedProducer,
            // Quota
            ProLimitReached = proLimitReached,
            ProScansToday   = proStatus?.ScansToday   ?? 0,
            DailyProLimit   = proStatus?.DailyLimit   ?? _settings.DailyProLimit,
            IsPro           = proStatus?.IsPro        ?? false,
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private const string FoodPairingPrompt = """
        Du er en sommelier-ekspert. Basert på vinen nedenfor, generer en kort vinbeskrivelse, matanbefalinger og tekniske smaksnotater.
        Returner KUN rå JSON (uten markdown-formatering).
        JSON-struktur:
        {
          "description": string,
          "foodPairings": string[],
          "technicalNotes": string
        }
        - description: 1-2 setninger som beskriver vinens karakter og stil på norsk
        - foodPairings: 3-5 konkrete matanbefalinger på norsk (f.eks. "Lammekoteletter", "Modnet parmesan")
        - technicalNotes: 1-2 setninger med tekniske smaksnotater på norsk (tanniner, syre, finish)
        """;

    private record FoodPairingResult(string[]? FoodPairings, string? TechnicalNotes, string? Description);

    private async Task<FoodPairingResult?> GetFoodPairingsViaChainAsync(
        string? wineName, string? producer, int? vintage, string? type, string? country,
        CancellationToken ct)
    {
        var userContent = $"Vin: {producer ?? ""} {wineName ?? ""}, {vintage?.ToString() ?? "ukjent årgang"}, {type ?? ""} {country ?? ""}";

        var chatResult = await _aiChain.ChatAsync(
            _settings.AiFallback.ExpertChatPriority,
            FoodPairingPrompt,
            userContent,
            ct);

        if (!chatResult.IsSuccess || chatResult.Answer is null)
        {
            _logger.LogWarning("OrchestratorService: AI chain food-pairing fallback failed");
            return null;
        }

        try
        {
            var rawJson = Regex.Replace(chatResult.Answer.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();
            var result = JsonSerializer.Deserialize<FoodPairingResult>(
                rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "OrchestratorService: food-pairing fallback via {Provider}: {Count} pairings for '{Wine}'",
                chatResult.ProviderName, result?.FoodPairings?.Length, wineName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OrchestratorService: failed to parse food-pairing response from {Provider}", chatResult.ProviderName);
            return null;
        }
    }

    private async Task<DedupMatch?> CheckDuplicateAsync(
        string producer, string name, int? vintage, Guid userId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Try exact match first, then fall back to fuzzy similarity (pg_trgm).
            // The CTE scores candidates: exact matches get 1.0, fuzzy get averaged similarity.
            return await conn.QueryFirstOrDefaultAsync<DedupMatch>(
                """
                WITH candidates AS (
                    SELECT
                        w.id,
                        CASE
                            WHEN LOWER(TRIM(w.producer)) = LOWER(TRIM(@Producer))
                             AND LOWER(TRIM(w.name))     = LOWER(TRIM(@Name))
                            THEN 1.0
                            ELSE (
                                similarity(LOWER(TRIM(w.producer)), LOWER(TRIM(@Producer)))
                              + similarity(LOWER(TRIM(w.name)),     LOWER(TRIM(@Name)))
                            ) / 2.0
                        END AS match_score
                    FROM wines w
                    WHERE COALESCE(w.vintage, -1) = COALESCE(@Vintage::INT, -1)
                      AND (
                          (LOWER(TRIM(w.producer)) = LOWER(TRIM(@Producer))
                           AND LOWER(TRIM(w.name)) = LOWER(TRIM(@Name)))
                          OR
                          (similarity(LOWER(TRIM(w.producer)), LOWER(TRIM(@Producer))) > 0.35
                           AND similarity(LOWER(TRIM(w.name)), LOWER(TRIM(@Name)))     > 0.25)
                      )
                )
                SELECT
                    c.id AS WineId,
                    (SELECT COUNT(*) FROM wine_logs WHERE wine_id = c.id AND user_id = @UserId) AS UserLogCount,
                    (SELECT rating    FROM wine_logs WHERE wine_id = c.id AND user_id = @UserId ORDER BY created_at DESC LIMIT 1) AS LastRating,
                    (SELECT tasted_at FROM wine_logs WHERE wine_id = c.id AND user_id = @UserId ORDER BY created_at DESC LIMIT 1) AS LastTastedAt
                FROM candidates c
                ORDER BY c.match_score DESC
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
