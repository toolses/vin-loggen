using Dapper;
using Npgsql;
using VinLoggen.Api.Services;

namespace VinLoggen.Api.Services;

public sealed class WineCatalogueService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<WineCatalogueService> _logger;

    private static readonly Dictionary<string, string> WineTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"]       = "Rød",
        ["white"]     = "Hvit",
        ["rosé"]      = "Rosé",
        ["rose"]      = "Rosé",
        ["sparkling"] = "Musserende",
        ["dessert"]   = "Dessert",
        ["orange"]    = "Oransje",
    };

    private static string NormalizeWineType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Rød";
        return WineTypeMap.TryGetValue(type, out var mapped) ? mapped : type;
    }

    public WineCatalogueService(NpgsqlDataSource dataSource, ILogger<WineCatalogueService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Upserts wines from search hits into the local catalogue.
    /// Returns a mapping of WineAPI external IDs to local wine IDs.
    /// </summary>
    public async Task<Dictionary<string, Guid>> UpsertFromSearchHitsAsync(
        List<WineApiSearchHitDto> hits, CancellationToken ct)
    {
        var saved = new Dictionary<string, Guid>();
        if (hits is not { Count: > 0 }) return saved;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.Id) || string.IsNullOrWhiteSpace(hit.Name))
                continue;

            try
            {
                var wineId = await UpsertWineCore(conn, ct,
                    externalId: hit.Id,
                    name: hit.Name,
                    producer: hit.Winery ?? "",
                    vintage: hit.Vintage,
                    type: NormalizeWineType(hit.Type),
                    country: hit.Country,
                    region: hit.Region);

                if (wineId.HasValue)
                    saved[hit.Id] = wineId.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WineCatalogueService: failed to upsert search hit {ExternalId}", hit.Id);
            }
        }

        return saved;
    }

    /// <summary>
    /// Upserts a wine from a detail response into the local catalogue.
    /// Returns the local wine ID, or null on failure.
    /// </summary>
    public async Task<Guid?> UpsertFromDetailAsync(WineApiDetailDto detail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(detail.Id) || string.IsNullOrWhiteSpace(detail.Name))
            return null;

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Check if we already have this wine by external ID
            var existingWineId = await conn.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(
                    "SELECT wine_id FROM wine_external_ids WHERE source = 'wineapi' AND external_id = @ExternalId LIMIT 1",
                    new { ExternalId = detail.Id },
                    cancellationToken: ct));

            if (existingWineId.HasValue)
            {
                // Update enrichment on the existing wine
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE wines SET
                            producer        = CASE WHEN TRIM(producer) = '' THEN @Producer ELSE producer END,
                            type            = COALESCE(@Type,            type),
                            country         = COALESCE(@Country,         country),
                            region          = COALESCE(@Region,          region),
                            grapes          = COALESCE(@Grapes,          grapes),
                            alcohol_content = COALESCE(@AlcoholContent,  alcohol_content),
                            food_pairings   = COALESCE(@FoodPairings,    food_pairings),
                            description     = COALESCE(@Description,     description),
                            technical_notes = COALESCE(@TechnicalNotes,  technical_notes)
                        WHERE id = @WineId
                        """,
                        new
                        {
                            WineId = existingWineId.Value,
                            Producer = detail.Winery ?? "",
                            Type = NormalizeWineType(detail.Type),
                            Country = detail.Country,
                            Region = detail.Region,
                            Grapes = detail.Grapes,
                            AlcoholContent = detail.AlcoholContent,
                            FoodPairings = detail.FoodPairings,
                            Description = detail.Description,
                            TechnicalNotes = detail.TechnicalNotes,
                        },
                        cancellationToken: ct));

                return existingWineId.Value;
            }

            // No existing mapping — upsert by (producer, name, vintage)
            var wineId = await UpsertWineWithEnrichment(conn, ct,
                name: detail.Name,
                producer: detail.Winery ?? "",
                vintage: detail.Vintage,
                type: NormalizeWineType(detail.Type),
                country: detail.Country,
                region: detail.Region,
                grapes: detail.Grapes,
                alcoholContent: detail.AlcoholContent,
                foodPairings: detail.FoodPairings,
                description: detail.Description,
                technicalNotes: detail.TechnicalNotes);

            if (wineId.HasValue)
                await UpsertExternalId(conn, ct, wineId.Value, detail.Id);

            return wineId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WineCatalogueService: failed to upsert detail {ExternalId}", detail.Id);
            return null;
        }
    }

    /// <summary>
    /// Upserts wines from an identify result (primary + suggestions) into the local catalogue.
    /// Returns a mapping of WineAPI external IDs to local wine IDs.
    /// </summary>
    public async Task<Dictionary<string, Guid>> UpsertFromIdentifyHitsAsync(
        WineApiIdentifyResultDto result, CancellationToken ct)
    {
        var saved = new Dictionary<string, Guid>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Collect primary wine + suggestions
        var hits = new List<WineApiIdentifyHitDto>();
        if (result.Wine is not null) hits.Add(result.Wine);
        if (result.Suggestions is { Count: > 0 }) hits.AddRange(result.Suggestions);

        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.Id) || string.IsNullOrWhiteSpace(hit.Name))
                continue;

            try
            {
                // WineApiIdentifyHitDto has no Winery field
                var wineId = await UpsertWineCore(conn, ct,
                    externalId: hit.Id,
                    name: hit.Name,
                    producer: "",
                    vintage: hit.Vintage,
                    type: NormalizeWineType(hit.Type),
                    country: hit.Country,
                    region: hit.Region);

                if (wineId.HasValue)
                    saved[hit.Id] = wineId.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WineCatalogueService: failed to upsert identify hit {ExternalId}", hit.Id);
            }
        }

        return saved;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task<Guid?> UpsertWineCore(
        NpgsqlConnection conn, CancellationToken ct,
        string externalId, string name, string producer,
        int? vintage, string type, string? country, string? region)
    {
        // Check existing external ID mapping first
        var existingWineId = await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                "SELECT wine_id FROM wine_external_ids WHERE source = 'wineapi' AND external_id = @ExternalId LIMIT 1",
                new { ExternalId = externalId },
                cancellationToken: ct));

        if (existingWineId.HasValue)
        {
            // Backfill basic fields if needed
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE wines SET
                        producer = CASE WHEN TRIM(producer) = '' THEN @Producer ELSE producer END,
                        type     = COALESCE(@Type,    type),
                        country  = COALESCE(@Country, country),
                        region   = COALESCE(@Region,  region)
                    WHERE id = @WineId
                    """,
                    new { WineId = existingWineId.Value, Producer = producer, Type = type, Country = country, Region = region },
                    cancellationToken: ct));

            return existingWineId.Value;
        }

        // Upsert by unique constraint (producer, name, vintage)
        var wineId = await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                """
                INSERT INTO wines (name, producer, vintage, type, country, region)
                VALUES (@Name, @Producer, @Vintage, @Type, @Country, @Region)
                ON CONFLICT (producer, name, vintage) DO UPDATE SET
                    type    = COALESCE(EXCLUDED.type,    wines.type),
                    country = COALESCE(EXCLUDED.country, wines.country),
                    region  = COALESCE(EXCLUDED.region,  wines.region)
                RETURNING id
                """,
                new { Name = name, Producer = producer, Vintage = vintage, Type = type, Country = country, Region = region },
                cancellationToken: ct));

        await UpsertExternalId(conn, ct, wineId, externalId);
        return wineId;
    }

    private static async Task<Guid?> UpsertWineWithEnrichment(
        NpgsqlConnection conn, CancellationToken ct,
        string name, string producer, int? vintage, string type,
        string? country, string? region, string[]? grapes, double? alcoholContent,
        string[]? foodPairings, string? description, string? technicalNotes)
    {
        return await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                """
                INSERT INTO wines (name, producer, vintage, type, country, region,
                                   grapes, alcohol_content, food_pairings, description, technical_notes)
                VALUES (@Name, @Producer, @Vintage, @Type, @Country, @Region,
                        @Grapes, @AlcoholContent, @FoodPairings, @Description, @TechnicalNotes)
                ON CONFLICT (producer, name, vintage) DO UPDATE SET
                    type            = COALESCE(EXCLUDED.type,            wines.type),
                    country         = COALESCE(EXCLUDED.country,         wines.country),
                    region          = COALESCE(EXCLUDED.region,          wines.region),
                    grapes          = COALESCE(EXCLUDED.grapes,          wines.grapes),
                    alcohol_content = COALESCE(EXCLUDED.alcohol_content, wines.alcohol_content),
                    food_pairings   = COALESCE(EXCLUDED.food_pairings,   wines.food_pairings),
                    description     = COALESCE(EXCLUDED.description,     wines.description),
                    technical_notes = COALESCE(EXCLUDED.technical_notes,  wines.technical_notes)
                RETURNING id
                """,
                new
                {
                    Name = name, Producer = producer, Vintage = vintage, Type = type,
                    Country = country, Region = region, Grapes = grapes,
                    AlcoholContent = alcoholContent, FoodPairings = foodPairings,
                    Description = description, TechnicalNotes = technicalNotes,
                },
                cancellationToken: ct));
    }

    private static async Task UpsertExternalId(
        NpgsqlConnection conn, CancellationToken ct, Guid wineId, string externalId)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO wine_external_ids (wine_id, source, external_id)
                VALUES (@WineId, 'wineapi', @ExternalId)
                ON CONFLICT (wine_id, source) DO UPDATE SET external_id = EXCLUDED.external_id
                """,
                new { WineId = wineId, ExternalId = externalId },
                cancellationToken: ct));
    }
}
