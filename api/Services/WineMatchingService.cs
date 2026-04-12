using System.Text.Json;
using Dapper;
using Npgsql;
using VinLoggen.Api.Models;

namespace VinLoggen.Api.Services;

public sealed class WineMatchingService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<WineMatchingService> _logger;

    public WineMatchingService(NpgsqlDataSource dataSource, ILogger<WineMatchingService> logger)
    {
        _dataSource = dataSource;
        _logger     = logger;
    }

    public async Task<WineSaveResponse> SaveAsync(Guid userId, WineSaveRequest request, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);

        try
        {
            // ── Step 1: Detect corrections ───────────────────────────────────
            var (originalFields, correctedFields) = DetectCorrections(request);
            var hasCorrections = originalFields.Count > 0;
            var identityChanged = IdentityFieldsChanged(request);

            // ── Step 2: Resolve wine_id ──────────────────────────────────────
            Guid wineId;
            bool newWineCreated = false;

            if (!identityChanged && request.ExistingWineId.HasValue)
            {
                // 2a: Identity unchanged + existing wine → reuse it
                wineId = request.ExistingWineId.Value;

                // Optionally backfill enrichment fields
                await MaybeUpdateEnrichment(conn, tx, wineId, request, ct);
            }
            else if (identityChanged)
            {
                // 2b: User changed identity fields → search for match with corrected values
                var matchId = await FindExactMatch(conn, tx, request, ct);

                if (matchId.HasValue)
                {
                    wineId = matchId.Value;
                    _logger.LogInformation(
                        "User {UserId} corrected wine identity — matched existing wine {WineId}",
                        userId, wineId);
                }
                else
                {
                    wineId = await InsertNewWine(conn, tx, request, ct);
                    newWineCreated = true;
                    _logger.LogInformation(
                        "User {UserId} corrected wine identity — created new wine {WineId}",
                        userId, wineId);
                }
            }
            else
            {
                // 2c: Manual entry (no existing wine, no original data) → upsert
                wineId = await UpsertWine(conn, tx, request, ct);
            }

            // ── Step 3: Insert wine_log ──────────────────────────────────────
            var logId = await conn.ExecuteScalarAsync<Guid>(
                new CommandDefinition(
                    """
                    INSERT INTO wine_logs (wine_id, user_id, rating, notes, image_url, thumbnail_url,
                                           tasted_at, location_name, location_lat, location_lng, location_type)
                    VALUES (@WineId, @UserId, @Rating, @Notes, @ImageUrl, @ThumbnailUrl,
                            @TastedAt::date, @LocationName, @LocationLat, @LocationLng, @LocationType)
                    RETURNING id
                    """,
                    new
                    {
                        WineId       = wineId,
                        UserId       = userId,
                        request.Rating,
                        request.Notes,
                        request.ImageUrl,
                        request.ThumbnailUrl,
                        request.TastedAt,
                        request.LocationName,
                        request.LocationLat,
                        request.LocationLng,
                        request.LocationType,
                    },
                    transaction: tx,
                    cancellationToken: ct));

            // ── Step 4: Log correction ───────────────────────────────────────
            if (hasCorrections && request.OriginalData is not null)
            {
                var originalJson  = JsonSerializer.Serialize(originalFields);
                var correctedJson = JsonSerializer.Serialize(correctedFields);

                await conn.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO data_corrections (user_id, wine_id, source, original_data, corrected_data)
                        VALUES (@UserId, @WineId, @Source, @OriginalData::jsonb, @CorrectedData::jsonb)
                        """,
                        new
                        {
                            UserId        = userId,
                            WineId        = wineId,
                            Source        = request.OriginalData.Source,
                            OriginalData  = originalJson,
                            CorrectedData = correctedJson,
                        },
                        transaction: tx,
                        cancellationToken: ct));
            }

            // ── Step 5: Commit and return ────────────────────────────────────
            await tx.CommitAsync(ct);

            return new WineSaveResponse(wineId, logId, hasCorrections, newWineCreated);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Correction detection ─────────────────────────────────────────────────

    private static (Dictionary<string, object?> original, Dictionary<string, object?> corrected)
        DetectCorrections(WineSaveRequest request)
    {
        var original  = new Dictionary<string, object?>();
        var corrected = new Dictionary<string, object?>();

        if (request.OriginalData is null) return (original, corrected);

        var orig = request.OriginalData;

        CompareString("name",     orig.Name,     request.Name,     original, corrected);
        CompareString("producer", orig.Producer,  request.Producer, original, corrected);
        CompareString("type",     orig.Type,      request.Type,     original, corrected);
        CompareString("country",  orig.Country,   request.Country,  original, corrected);
        CompareString("region",   orig.Region,    request.Region,   original, corrected);

        if (orig.Vintage != request.Vintage)
        {
            original["vintage"]  = orig.Vintage;
            corrected["vintage"] = request.Vintage;
        }

        if (orig.AlcoholContent != request.AlcoholContent)
        {
            original["alcoholContent"]  = orig.AlcoholContent;
            corrected["alcoholContent"] = request.AlcoholContent;
        }

        if (!GrapesEqual(orig.Grapes, request.Grapes))
        {
            original["grapes"]  = orig.Grapes;
            corrected["grapes"] = request.Grapes;
        }

        return (original, corrected);
    }

    private static void CompareString(
        string key, string? origVal, string? newVal,
        Dictionary<string, object?> original, Dictionary<string, object?> corrected)
    {
        var a = (origVal ?? "").Trim();
        var b = (newVal ?? "").Trim();
        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            original[key]  = origVal;
            corrected[key] = newVal;
        }
    }

    private static bool GrapesEqual(string[]? a, string[]? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;

        var sortedA = a.Select(g => g.Trim().ToLowerInvariant()).OrderBy(g => g).ToList();
        var sortedB = b.Select(g => g.Trim().ToLowerInvariant()).OrderBy(g => g).ToList();
        return sortedA.SequenceEqual(sortedB);
    }

    private bool IdentityFieldsChanged(WineSaveRequest request)
    {
        if (request.OriginalData is null) return false;

        var orig = request.OriginalData;
        var nameChanged = !string.Equals(
            (orig.Name ?? "").Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        var producerChanged = !string.Equals(
            (orig.Producer ?? "").Trim(), request.Producer.Trim(), StringComparison.OrdinalIgnoreCase);
        var vintageChanged = orig.Vintage != request.Vintage;

        return nameChanged || producerChanged || vintageChanged;
    }

    // ── Wine resolution ──────────────────────────────────────────────────────

    private static async Task<Guid?> FindExactMatch(
        NpgsqlConnection conn, NpgsqlTransaction tx, WineSaveRequest request, CancellationToken ct)
    {
        return await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                """
                SELECT id FROM wines
                WHERE LOWER(TRIM(producer)) = LOWER(TRIM(@Producer))
                  AND LOWER(TRIM(name))     = LOWER(TRIM(@Name))
                  AND COALESCE(vintage, -1) = COALESCE(@Vintage, -1)
                LIMIT 1
                """,
                new { request.Producer, request.Name, request.Vintage },
                transaction: tx,
                cancellationToken: ct));
    }

    private static async Task<Guid> InsertNewWine(
        NpgsqlConnection conn, NpgsqlTransaction tx, WineSaveRequest request, CancellationToken ct)
    {
        return await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                """
                INSERT INTO wines (name, producer, vintage, type, country, region,
                                   grapes, alcohol_content, external_source_id,
                                   food_pairings, description, technical_notes)
                VALUES (@Name, @Producer, @Vintage, @Type, @Country, @Region,
                        @Grapes, @AlcoholContent, @ExternalSourceId,
                        @FoodPairings, @Description, @TechnicalNotes)
                RETURNING id
                """,
                new
                {
                    request.Name, request.Producer, request.Vintage, request.Type,
                    request.Country, request.Region, request.Grapes, request.AlcoholContent,
                    request.ExternalSourceId, request.FoodPairings, request.Description,
                    request.TechnicalNotes,
                },
                transaction: tx,
                cancellationToken: ct));
    }

    private static async Task<Guid> UpsertWine(
        NpgsqlConnection conn, NpgsqlTransaction tx, WineSaveRequest request, CancellationToken ct)
    {
        return await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                """
                INSERT INTO wines (name, producer, vintage, type, country, region,
                                   grapes, alcohol_content, external_source_id,
                                   food_pairings, description, technical_notes)
                VALUES (@Name, @Producer, @Vintage, @Type, @Country, @Region,
                        @Grapes, @AlcoholContent, @ExternalSourceId,
                        @FoodPairings, @Description, @TechnicalNotes)
                ON CONFLICT (producer, name, vintage) DO UPDATE SET
                    food_pairings    = COALESCE(EXCLUDED.food_pairings,    wines.food_pairings),
                    description      = COALESCE(EXCLUDED.description,      wines.description),
                    technical_notes  = COALESCE(EXCLUDED.technical_notes,  wines.technical_notes),
                    external_source_id = COALESCE(EXCLUDED.external_source_id, wines.external_source_id)
                RETURNING id
                """,
                new
                {
                    request.Name, request.Producer, request.Vintage, request.Type,
                    request.Country, request.Region, request.Grapes, request.AlcoholContent,
                    request.ExternalSourceId, request.FoodPairings, request.Description,
                    request.TechnicalNotes,
                },
                transaction: tx,
                cancellationToken: ct));
    }

    private static async Task MaybeUpdateEnrichment(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid wineId,
        WineSaveRequest request, CancellationToken ct)
    {
        if (request.FoodPairings is null && request.Description is null
            && request.TechnicalNotes is null && request.ExternalSourceId is null)
            return;

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE wines SET
                    food_pairings      = COALESCE(@FoodPairings,      food_pairings),
                    description        = COALESCE(@Description,        description),
                    technical_notes    = COALESCE(@TechnicalNotes,     technical_notes),
                    external_source_id = COALESCE(@ExternalSourceId,   external_source_id)
                WHERE id = @WineId
                """,
                new
                {
                    WineId = wineId,
                    request.FoodPairings, request.Description,
                    request.TechnicalNotes, request.ExternalSourceId,
                },
                transaction: tx,
                cancellationToken: ct));
    }
}
