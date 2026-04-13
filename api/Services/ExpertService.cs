using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using VinLoggen.Api.Configuration;
using VinLoggen.Api.Models;
using VinLoggen.Api.Services.AiProviders;

namespace VinLoggen.Api.Services;

public sealed class ExpertService : IExpertService
{
    private const string WineDelimiter = "---WINES---";

    private const string SystemInstructionTemplate = """
        Du er VinSomm-eksperten — en personlig og profesjonell AI-vinkelner.
        Dagens dato er {DATE}.
        Bruk vedlagte data fra vår lokale vinkatalog og brukerens smaksprofil
        for å gi skreddersydde anbefalinger. Hvis katalogen ikke dekker det
        brukeren spør om, anbefal viner fra din egen kunnskap — du er IKKE
        begrenset til katalogen.

        Regler:
        - Svar alltid på norsk.
        - Vær vennlig, konsis og kompetent.
        - Hvis du foreslår en vin fra katalogen, nevn HVORFOR den passer brukerens smak.
        - Hvis katalogen mangler relevante viner, foreslå konkrete viner du kjenner til
          (bruk ekte vin- og produsentnavn). Merk dem med «whyRecommended».
        - Hvis brukerens smaksprofil er tilgjengelig, bruk den til å gi personlige råd.
        - Hvis du ikke har nok informasjon til å svare, si det ærlig.
        - Bruk brukerens fornavn når det passer.
        - Foretrekk nyere årganger (siste 4 år) med mindre brukeren spesifikt ber om noe annet.

        VIKTIG — Etter ditt svar SKAL du legge til en seksjon med vinene du anbefaler.
        Bruk nøyaktig dette formatet:

        [ditt markdown-svar her]

        ---WINES---
        [{"name":"Vinnavn","producer":"Produsent","vintage":2024,"type":"Rød","country":"Italia","region":"Toscana","whyRecommended":"Kort grunn"}]

        Regler for ---WINES---:
        - Plasser «---WINES---» på en egen linje etter svaret ditt.
        - Etter delimiteren: en JSON-array (ingen markdown-formatering, ingen code fences).
        - Bruk nøyaktig samme navn og produsent som i katalogdataen når vinen finnes der.
          Ellers bruk ekte vin- og produsentnavn fra din egen kunnskap.
        - «type» skal være en av: «Rød», «Hvit», «Rosé», «Musserende», «Oransje», «Dessert».
          Champagne, Cava, Prosecco og Crémant er «Musserende».
          Portvin, Sherry, Madeira og Marsala er «Dessert».
        - «whyRecommended»: Forklar kort HVORFOR vinen passer.
        - Foreslå alltid minst 2–3 viner når spørsmålet handler om vin, selv om katalogen er tom.
        - Hvis spørsmålet ikke handler om viner, sett en tom array: []
        """;

    private const string EnrichmentPrompt = """
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

    private readonly AiProviderChain _aiChain;
    private readonly IntegrationSettings _settings;
    private readonly IConfiguration _configuration;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IProUsageService _proUsage;
    private readonly IWineApiService _wineApi;
    private readonly IApiUsageService _apiUsage;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExpertService> _logger;

    public ExpertService(
        AiProviderChain         aiChain,
        IConfiguration          configuration,
        NpgsqlDataSource        dataSource,
        IProUsageService        proUsage,
        IWineApiService         wineApi,
        IApiUsageService        apiUsage,
        IntegrationSettings     settings,
        IMemoryCache            cache,
        ILogger<ExpertService>  logger)
    {
        _aiChain       = aiChain;
        _configuration = configuration;
        _dataSource    = dataSource;
        _proUsage      = proUsage;
        _wineApi       = wineApi;
        _apiUsage      = apiUsage;
        _settings      = settings;
        _cache         = cache;
        _logger        = logger;
    }

    public Task<ExpertResponse> AskAsync(Guid userId, ExpertRequest request, CancellationToken ct)
        => AskCoreAsync(userId, request, onProgress: null, ct);

    public Task<ExpertResponse> AskStreamAsync(Guid userId, ExpertRequest request, Func<string, Task> onProgress, CancellationToken ct)
        => AskCoreAsync(userId, request, onProgress, ct);

    private async Task<ExpertResponse> AskCoreAsync(
        Guid userId, ExpertRequest request, Func<string, Task>? onProgress, CancellationToken ct)
    {
        async Task Progress(string status) { if (onProgress is not null) await onProgress(status); }

        // 1. Check quota
        var proStatus = await _proUsage.GetStatusAsync(userId, ct);
        if (!proStatus.CanUsePro)
        {
            throw new ExpertQuotaExceededException(
                proStatus.ScansToday, proStatus.DailyLimit, proStatus.ScansRemaining);
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 2. Search local wines DB for context
        await Progress("Søker i vinkatalogen …");
        var catalogWines = await SearchCatalogWinesAsync(conn, request.Question, ct);

        // 3. Fetch user's taste profile
        var tasteProfileJson = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT taste_profile_json::text FROM user_profiles WHERE user_id = @UserId",
            new { UserId = userId });

        // 4. Fetch 3 most recent tasting logs
        var recentTastings = await conn.QueryAsync<RecentTasting>(
            """
            SELECT w.name     AS WineName,
                   w.producer AS Producer,
                   w.type     AS Type,
                   w.country  AS Country,
                   wl.rating  AS Rating,
                   wl.notes   AS Notes,
                   wl.tasted_at AS TastedAt
            FROM wine_logs wl
            JOIN wines w ON w.id = wl.wine_id
            WHERE wl.user_id = @UserId
            ORDER BY wl.created_at DESC
            LIMIT 3
            """,
            new { UserId = userId });

        // 5. Build prompt context (no globalMatch — WineAPI is now used for enrichment)
        var contextJson = JsonSerializer.Serialize(new
        {
            question       = request.Question,
            catalogWines   = catalogWines,
            tasteProfile   = tasteProfileJson is not null
                ? JsonSerializer.Deserialize<object>(tasteProfileJson)
                : null,
            recentTastings = recentTastings,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // 6. Call AI via provider chain (DeepSeek → Gemini fallback)
        await Progress("Spør AI-sommelieren …");
        var systemInstruction = SystemInstructionTemplate.Replace("{DATE}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        var chatResult = await _aiChain.ChatAsync(
            _settings.AiFallback.ExpertChatPriority,
            systemInstruction,
            contextJson,
            ct);

        if (!chatResult.IsSuccess || chatResult.Answer is null)
        {
            return new ExpertResponse(
                "Beklager, jeg fikk ikke kontakt med AI-tjenesten. Prøv igjen.",
                null,
                proStatus.ScansToday,
                proStatus.DailyLimit,
                proStatus.ScansRemaining);
        }

        _logger.LogInformation("ExpertService: answered via {Provider}", chatResult.ProviderName);

        // 7. Parse AI response: extract answer text + wine suggestions
        var parsed = ParseAiResponse(chatResult.Answer);

        // 8. Enrich AI-suggested wines via WineAPI, with AI fallback for gaps
        if (parsed.Wines.Length > 0)
        {
            await Progress("Henter vindetaljer fra WineAPI …");
        }
        var enrichedSuggestions = await EnrichWinesAsync(parsed.Wines, onProgress, ct);

        // 9. Build merged wine references (catalog + enriched AI suggestions)
        var refs = BuildEnrichedReferences(catalogWines, parsed.Wines, enrichedSuggestions);

        // 10. Charge quota (only on success)
        await _proUsage.IncrementAsync(userId, ct);
        var updatedStatus = await _proUsage.GetStatusAsync(userId, ct);

        // 11. Background catalogue upsert for WineAPI-enriched wines
        foreach (var (suggestion, enrichment) in parsed.Wines.Zip(enrichedSuggestions))
        {
            if (enrichment?.ExternalId is not null)
            {
                _ = TryUpsertEnrichedWineAsync(suggestion, enrichment);
            }
        }

        // 12. Persist conversation (best-effort — failures don't break the response)
        Guid? sessionId = null;
        Guid[]? suggestionIds = null;
        var wineRefs = refs.Count > 0 ? refs.ToArray() : null;

        try
        {
            (sessionId, suggestionIds) = await PersistConversationAsync(
                conn, userId, request, parsed.Answer, chatResult.ProviderName, wineRefs, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExpertService: conversation persistence failed — user still gets the answer");
        }

        return new ExpertResponse(
            parsed.Answer,
            wineRefs,
            updatedStatus.ScansToday,
            updatedStatus.DailyLimit,
            updatedStatus.ScansRemaining,
            chatResult.ProviderName,
            sessionId,
            suggestionIds);
    }

    // ── Conversation persistence ─────────────────────────────────────────────

    private async Task<(Guid SessionId, Guid[]? SuggestionIds)> PersistConversationAsync(
        NpgsqlConnection conn, Guid userId, ExpertRequest request,
        string answer, string? modelUsed, ExpertWineReference[]? wines, CancellationToken ct)
    {
        Guid sessionId;

        if (request.SessionId is not null)
        {
            // Verify ownership and reuse existing session
            var existing = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT id FROM expert_sessions WHERE id = @Id AND user_id = @UserId",
                new { Id = request.SessionId.Value, UserId = userId });

            if (existing is null)
                throw new InvalidOperationException($"Session {request.SessionId} not found or not owned by user");

            sessionId = existing.Value;

            await conn.ExecuteAsync(
                "UPDATE expert_sessions SET updated_at = NOW() WHERE id = @Id",
                new { Id = sessionId });
        }
        else
        {
            // Create new session with title from the first question
            var title = request.Question.Length > 80
                ? request.Question[..80].TrimEnd() + "…"
                : request.Question;
            title = title.ReplaceLineEndings(" ");

            sessionId = await conn.ExecuteScalarAsync<Guid>(
                """
                INSERT INTO expert_sessions (user_id, title)
                VALUES (@UserId, @Title)
                RETURNING id
                """,
                new { UserId = userId, Title = title });
        }

        // Insert user message
        await conn.ExecuteAsync(
            """
            INSERT INTO expert_messages (session_id, role, content)
            VALUES (@SessionId, 'user', @Content)
            """,
            new { SessionId = sessionId, Content = request.Question });

        // Insert assistant message
        var assistantMessageId = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO expert_messages (session_id, role, content, model_used)
            VALUES (@SessionId, 'assistant', @Content, @ModelUsed)
            RETURNING id
            """,
            new { SessionId = sessionId, Content = answer, ModelUsed = modelUsed });

        // Insert wine suggestions
        Guid[]? suggestionIds = null;
        if (wines is { Length: > 0 })
        {
            suggestionIds = new Guid[wines.Length];
            for (int i = 0; i < wines.Length; i++)
            {
                var wine = wines[i];
                var wineId = wine.Id != Guid.Empty ? wine.Id : (Guid?)null;
                var wineDataJson = JsonSerializer.Serialize(wine,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                suggestionIds[i] = await conn.ExecuteScalarAsync<Guid>(
                    """
                    INSERT INTO expert_wine_suggestions (message_id, wine_id, wine_data)
                    VALUES (@MessageId, @WineId, @WineData::jsonb)
                    RETURNING id
                    """,
                    new { MessageId = assistantMessageId, WineId = wineId, WineData = wineDataJson });
            }
        }

        _logger.LogInformation(
            "ExpertService: persisted conversation session {SessionId} ({WineCount} suggestions)",
            sessionId, wines?.Length ?? 0);

        return (sessionId, suggestionIds);
    }

    // ── Session queries ──────────────────────────────────────────────────────

    public async Task<IEnumerable<ExpertSessionSummary>> GetSessionsAsync(
        Guid userId, int limit, int offset, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.QueryAsync<ExpertSessionSummary>(
            """
            SELECT es.id          AS Id,
                   es.title       AS Title,
                   es.created_at  AS CreatedAt,
                   es.updated_at  AS UpdatedAt,
                   (SELECT COUNT(*) FROM expert_messages WHERE session_id = es.id) AS MessageCount
            FROM expert_sessions es
            WHERE es.user_id = @UserId
            ORDER BY es.updated_at DESC
            LIMIT @Limit OFFSET @Offset
            """,
            new { UserId = userId, Limit = limit, Offset = offset });
    }

    public async Task<ExpertSessionDetail?> GetSessionAsync(
        Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Load session header
        var session = await conn.QuerySingleOrDefaultAsync<(Guid Id, string? Title, DateTime CreatedAt)?>(
            """
            SELECT id AS Id, title AS Title, created_at AS CreatedAt
            FROM expert_sessions
            WHERE id = @SessionId AND user_id = @UserId
            """,
            new { SessionId = sessionId, UserId = userId });

        if (session is null)
            return null;

        // Load all messages
        var messages = (await conn.QueryAsync<ExpertMessageRow>(
            """
            SELECT id AS Id, role AS Role, content AS Content,
                   model_used AS ModelUsed, created_at AS CreatedAt
            FROM expert_messages
            WHERE session_id = @SessionId
            ORDER BY created_at ASC
            """,
            new { SessionId = sessionId })).AsList();

        // Load all wine suggestions for these messages
        var messageIds = messages.Select(m => m.Id).ToArray();
        var suggestions = messageIds.Length > 0
            ? (await conn.QueryAsync<ExpertWineSuggestionRow>(
                """
                SELECT id AS Id, message_id AS MessageId, wine_id AS WineId,
                       wine_data::text AS WineDataJson, feedback AS Feedback
                FROM expert_wine_suggestions
                WHERE message_id = ANY(@MessageIds)
                """,
                new { MessageIds = messageIds })).ToLookup(s => s.MessageId)
            : Enumerable.Empty<ExpertWineSuggestionRow>().ToLookup(s => s.MessageId);

        // Assemble DTOs
        var messageDtos = messages.Select(m => new ExpertMessageDto(
            m.Id, m.Role, m.Content, m.ModelUsed, m.CreatedAt,
            suggestions[m.Id].Select(s => new ExpertWineSuggestionDto(
                s.Id, s.WineId, s.WineDataJson, s.Feedback)).ToArray()
        )).ToArray();

        return new ExpertSessionDetail(
            session.Value.Id, session.Value.Title, session.Value.CreatedAt, messageDtos);
    }

    public async Task<bool> SetWineFeedbackAsync(
        Guid userId, Guid suggestionId, short feedback, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            """
            UPDATE expert_wine_suggestions
            SET feedback = @Feedback
            WHERE id = @SuggestionId
              AND message_id IN (
                  SELECT em.id FROM expert_messages em
                  JOIN expert_sessions es ON es.id = em.session_id
                  WHERE es.user_id = @UserId
              )
            """,
            new { SuggestionId = suggestionId, Feedback = feedback, UserId = userId });
        return rows > 0;
    }

    public async Task<bool> DeleteSessionAsync(
        Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            "DELETE FROM expert_sessions WHERE id = @SessionId AND user_id = @UserId",
            new { SessionId = sessionId, UserId = userId });
        return rows > 0;
    }

    // ── AI response parsing ─────────────────────────────────────────────────────

    private ParsedAiResponse ParseAiResponse(string rawAnswer)
    {
        var delimiterIndex = rawAnswer.IndexOf(WineDelimiter, StringComparison.Ordinal);
        if (delimiterIndex < 0)
        {
            _logger.LogDebug("ExpertService: no ---WINES--- delimiter found, using full response as answer");
            return new ParsedAiResponse(rawAnswer.Trim(), []);
        }

        var answerText = rawAnswer[..delimiterIndex].Trim();
        var winesJson = rawAnswer[(delimiterIndex + WineDelimiter.Length)..].Trim();

        // Strip markdown code fences (same pattern as WineOrchestratorService/GeminiService)
        winesJson = Regex.Replace(winesJson, @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        if (string.IsNullOrWhiteSpace(winesJson))
            return new ParsedAiResponse(answerText, []);

        try
        {
            var wines = JsonSerializer.Deserialize<AiWineSuggestion[]>(
                winesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation("ExpertService: parsed {Count} wine suggestions from AI response", wines?.Length ?? 0);
            return new ParsedAiResponse(answerText, wines ?? []);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ExpertService: failed to parse wines JSON after delimiter, falling back");
            return new ParsedAiResponse(answerText, []);
        }
    }

    // ── Enrichment pipeline (mirrors WineOrchestratorService pattern) ────────────

    private async Task<WineApiService.WineEnrichment?[]> EnrichWinesAsync(
        AiWineSuggestion[] aiWines, Func<string, Task>? onProgress, CancellationToken ct)
    {
        if (aiWines.Length == 0)
            return [];

        async Task Progress(string status) { if (onProgress is not null) await onProgress(status); }

        var results = new WineApiService.WineEnrichment?[aiWines.Length];
        var usedAiFallback = false;

        for (int i = 0; i < aiWines.Length; i++)
        {
            var wine = aiWines[i];
            if (string.IsNullOrWhiteSpace(wine.Name))
            {
                results[i] = null;
                continue;
            }

            // Step a: Try WineAPI enrichment
            var enrichment = await _wineApi.FindAsync(
                wine.Producer ?? "", wine.Name, wine.Vintage, ct);

            // Step b: AI fallback if WineAPI has gaps (missing food pairings OR description)
            if (enrichment?.FoodPairings is not { Length: > 0 } || string.IsNullOrWhiteSpace(enrichment?.Description))
            {
                if (!usedAiFallback)
                {
                    await Progress("WineAPI hadde ikke alt — supplerer med AI …");
                    usedAiFallback = true;
                }

                var aiFallback = await GetWineEnrichmentViaAiAsync(wine, ct);

                if (aiFallback is not null)
                {
                    enrichment = enrichment is not null
                        ? enrichment with
                        {
                            FoodPairings   = enrichment.FoodPairings is { Length: > 0 } ? enrichment.FoodPairings : aiFallback.FoodPairings,
                            TechnicalNotes = enrichment.TechnicalNotes ?? aiFallback.TechnicalNotes,
                            Description    = enrichment.Description ?? aiFallback.Description,
                        }
                        : new WineApiService.WineEnrichment(
                            ExternalId:     null,
                            Description:    aiFallback.Description,
                            FoodPairings:   aiFallback.FoodPairings,
                            TechnicalNotes: aiFallback.TechnicalNotes,
                            AlcoholContent: null,
                            Grapes:         null);
                }
            }

            results[i] = enrichment;
        }

        return results;
    }

    private async Task<EnrichmentFallbackResult?> GetWineEnrichmentViaAiAsync(
        AiWineSuggestion wine, CancellationToken ct)
    {
        var userContent = $"Vin: {wine.Producer ?? ""} {wine.Name ?? ""}, " +
                          $"{wine.Vintage?.ToString() ?? "ukjent årgang"}, " +
                          $"{wine.Type ?? ""} {wine.Country ?? ""}";

        var chatResult = await _aiChain.ChatAsync(
            _settings.AiFallback.ExpertChatPriority,
            EnrichmentPrompt,
            userContent,
            ct);

        if (!chatResult.IsSuccess || chatResult.Answer is null)
        {
            _logger.LogWarning("ExpertService: AI enrichment fallback failed for '{Wine}'", wine.Name);
            return null;
        }

        try
        {
            var rawJson = Regex.Replace(
                chatResult.Answer.Trim(),
                @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

            var result = JsonSerializer.Deserialize<EnrichmentFallbackResult>(
                rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "ExpertService: AI enrichment via {Provider}: {Count} pairings for '{Wine}'",
                chatResult.ProviderName, result?.FoodPairings?.Length, wine.Name);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ExpertService: failed to parse AI enrichment response for '{Wine}'", wine.Name);
            return null;
        }
    }

    // ── Reference building ──────────────────────────────────────────────────────

    private List<ExpertWineReference> BuildEnrichedReferences(
        IReadOnlyList<CatalogWineRow> catalogWines,
        AiWineSuggestion[] aiWines,
        WineApiService.WineEnrichment?[] enrichments)
    {
        var refs = new List<ExpertWineReference>();

        // Catalog wines first (verified data from local DB)
        foreach (var w in catalogWines)
        {
            var aiReason = FindAiReason(aiWines, w.Name, w.Producer);
            refs.Add(new ExpertWineReference(
                w.Id, w.Name, w.Producer, w.Vintage, w.Type, w.Country,
                Region: w.Region,
                FoodPairings: w.FoodPairings,
                Description: w.Description,
                WhyRecommended: aiReason,
                Source: "catalog"));
        }

        // AI-suggested wines enriched via WineAPI/AI fallback
        for (int i = 0; i < aiWines.Length; i++)
        {
            var suggestion = aiWines[i];
            if (string.IsNullOrWhiteSpace(suggestion.Name))
                continue;

            // Skip if already matched to a catalog wine
            if (refs.Any(r => IsNameMatch(r.Name, r.Producer, suggestion.Name!, suggestion.Producer)))
                continue;

            var enrichment = i < enrichments.Length ? enrichments[i] : null;
            var source = enrichment?.ExternalId is not null ? "wineapi" : "ai";

            refs.Add(new ExpertWineReference(
                Id: Guid.Empty,
                Name: enrichment?.SuggestedName ?? suggestion.Name!,
                Producer: enrichment?.SuggestedProducer ?? suggestion.Producer ?? "",
                Vintage: suggestion.Vintage,
                Type: suggestion.Type ?? "",
                Country: suggestion.Country,
                Region: suggestion.Region,
                FoodPairings: enrichment?.FoodPairings,
                Description: enrichment?.Description,
                TechnicalNotes: enrichment?.TechnicalNotes,
                WhyRecommended: suggestion.WhyRecommended,
                Source: source));
        }

        return refs;
    }

    private static string? FindAiReason(AiWineSuggestion[] aiWines, string name, string producer)
    {
        return aiWines
            .FirstOrDefault(w => IsNameMatch(name, producer, w.Name, w.Producer))
            ?.WhyRecommended;
    }

    private static bool IsNameMatch(string name1, string producer1, string? name2, string? producer2)
    {
        if (string.IsNullOrWhiteSpace(name2)) return false;

        var n1 = name1.Trim().ToLowerInvariant();
        var n2 = name2.Trim().ToLowerInvariant();
        var p1 = producer1.Trim().ToLowerInvariant();
        var p2 = (producer2 ?? "").Trim().ToLowerInvariant();

        return (n1.Contains(n2) || n2.Contains(n1))
            && (string.IsNullOrEmpty(p2) || p1.Contains(p2) || p2.Contains(p1));
    }

    /// <summary>
    /// Returns true when the enrichment is missing fields that the /wines/{id}
    /// details endpoint might provide (alcohol, grapes, description, food pairings, technical notes).
    /// </summary>
    private static bool IsMissingDetailFields(WineApiService.WineEnrichment e)
        => e.AlcoholContent is null
        || e.Grapes is not { Length: > 0 }
        || string.IsNullOrWhiteSpace(e.Description)
        || e.FoodPairings is not { Length: > 0 }
        || string.IsNullOrWhiteSpace(e.TechnicalNotes);

    // ── Background catalogue upsert ─────────────────────────────────────────────

    private async Task TryUpsertEnrichedWineAsync(AiWineSuggestion suggestion, WineApiService.WineEnrichment enrichment)
    {
        try
        {
            // If we have a WineAPI ID but are missing detail fields, fetch full details first
            if (enrichment.ExternalId is not null && IsMissingDetailFields(enrichment))
            {
                var details = await _wineApi.GetDetailsAsync(enrichment.ExternalId, CancellationToken.None);
                if (details is not null)
                {
                    enrichment = enrichment with
                    {
                        AlcoholContent = enrichment.AlcoholContent ?? details.AlcoholContent,
                        Grapes         = enrichment.Grapes is { Length: > 0 } ? enrichment.Grapes : details.Grapes,
                        Description    = enrichment.Description ?? details.Description,
                        FoodPairings   = enrichment.FoodPairings is { Length: > 0 } ? enrichment.FoodPairings : details.FoodPairings,
                        TechnicalNotes = enrichment.TechnicalNotes ?? details.TechnicalNotes,
                    };
                    _logger.LogInformation(
                        "ExpertService: filled gaps via GetDetails for '{Name}' (alcohol={Alcohol}, grapes={Grapes})",
                        suggestion.Name, enrichment.AlcoholContent, enrichment.Grapes?.Length);
                }
            }

            await using var conn = await _dataSource.OpenConnectionAsync();
            var wineId = await conn.ExecuteScalarAsync<Guid>(
                """
                INSERT INTO wines (name, producer, vintage, type, country, region,
                                   grapes, alcohol_content, external_source_id,
                                   food_pairings, description, technical_notes)
                VALUES (@Name, @Producer, @Vintage, @Type, @Country, @Region,
                        @Grapes, @AlcoholContent, @ExternalSourceId,
                        @FoodPairings, @Description, @TechnicalNotes)
                ON CONFLICT (producer, name, vintage) DO UPDATE SET
                    grapes             = COALESCE(EXCLUDED.grapes,             wines.grapes),
                    food_pairings      = COALESCE(EXCLUDED.food_pairings,      wines.food_pairings),
                    description        = COALESCE(EXCLUDED.description,        wines.description),
                    technical_notes    = COALESCE(EXCLUDED.technical_notes,    wines.technical_notes),
                    external_source_id = COALESCE(EXCLUDED.external_source_id, wines.external_source_id),
                    alcohol_content    = COALESCE(EXCLUDED.alcohol_content,    wines.alcohol_content)
                RETURNING id
                """,
                new
                {
                    Name             = enrichment.SuggestedName ?? suggestion.Name ?? "",
                    Producer         = enrichment.SuggestedProducer ?? suggestion.Producer ?? "",
                    Vintage          = suggestion.Vintage,
                    Type             = suggestion.Type ?? "Rød",
                    Country          = suggestion.Country,
                    Region           = suggestion.Region,
                    Grapes           = enrichment.Grapes,
                    AlcoholContent   = enrichment.AlcoholContent,
                    ExternalSourceId = enrichment.ExternalId,
                    FoodPairings     = enrichment.FoodPairings,
                    Description      = enrichment.Description,
                    TechnicalNotes   = enrichment.TechnicalNotes,
                });

            // Save external ID to mapping table
            if (enrichment.ExternalId is not null)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO wine_external_ids (wine_id, source, external_id)
                    VALUES (@WineId, 'wineapi', @ExternalId)
                    ON CONFLICT (wine_id, source) DO UPDATE SET external_id = EXCLUDED.external_id
                    """,
                    new { WineId = wineId, ExternalId = enrichment.ExternalId });
            }

            _logger.LogInformation(
                "ExpertService: upserted wine '{Name}' by '{Producer}' ({Vintage}) into catalogue",
                suggestion.Name, suggestion.Producer, suggestion.Vintage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ExpertService: failed to upsert wine '{Name}' — skipping", suggestion.Name);
        }
    }

    // ── Catalog search ──────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<CatalogWineRow>> SearchCatalogWinesAsync(
        NpgsqlConnection conn, string question, CancellationToken ct)
    {
        var words = Regex.Matches(question.ToLowerInvariant(), @"\b[a-zæøåà-ü]{3,}\b")
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Distinct()
            .ToList();

        if (words.Count == 0)
        {
            var fallback = await conn.QueryAsync<CatalogWineRow>(
                """
                SELECT id AS Id, name AS Name, producer AS Producer, vintage AS Vintage,
                       type AS Type, country AS Country, region AS Region,
                       food_pairings AS FoodPairings, description AS Description
                FROM wines
                ORDER BY created_at DESC
                LIMIT 5
                """);
            return fallback.AsList();
        }

        var likePattern = string.Join(" ", words);
        var results = await conn.QueryAsync<CatalogWineRow>(
            """
            SELECT id AS Id, name AS Name, producer AS Producer, vintage AS Vintage,
                   type AS Type, country AS Country, region AS Region,
                   food_pairings AS FoodPairings, description AS Description
            FROM wines
            WHERE name     ILIKE '%' || @Search || '%'
               OR producer ILIKE '%' || @Search || '%'
               OR region   ILIKE '%' || @Search || '%'
               OR type     ILIKE '%' || @Search || '%'
               OR country  ILIKE '%' || @Search || '%'
               OR array_to_string(grapes, ' ') ILIKE '%' || @Search || '%'
            ORDER BY
                SIMILARITY(LOWER(name || ' ' || producer || ' ' || COALESCE(region, '') || ' ' || COALESCE(type, '')),
                           LOWER(@Search)) DESC
            LIMIT 5
            """,
            new { Search = likePattern });

        var list = results.AsList();

        if (list.Count == 0 && words.Count > 1)
        {
            var orConditions = string.Join(" OR ",
                words.Select((_, i) => $"name ILIKE '%' || @W{i} || '%' OR producer ILIKE '%' || @W{i} || '%' OR region ILIKE '%' || @W{i} || '%'"));

            var parameters = new DynamicParameters();
            for (int i = 0; i < words.Count; i++)
                parameters.Add($"W{i}", words[i]);

            var fallback = await conn.QueryAsync<CatalogWineRow>(
                $"""
                SELECT id AS Id, name AS Name, producer AS Producer, vintage AS Vintage,
                       type AS Type, country AS Country, region AS Region,
                       food_pairings AS FoodPairings, description AS Description
                FROM wines
                WHERE {orConditions}
                LIMIT 5
                """,
                parameters);
            list = fallback.AsList();
        }

        return list;
    }

    // ── Private DTOs ────────────────────────────────────────────────────────────

    private record AiWineSuggestion(
        string? Name,
        string? Producer,
        int?    Vintage,
        string? Type,
        string? Country,
        string? Region,
        string? WhyRecommended);

    private record ParsedAiResponse(string Answer, AiWineSuggestion[] Wines);

    private record EnrichmentFallbackResult(string[]? FoodPairings, string? TechnicalNotes, string? Description);

    private record CatalogWineRow(
        Guid Id, string Name, string Producer, int? Vintage,
        string Type, string? Country, string? Region,
        string[]? FoodPairings, string? Description);

    private record RecentTasting(
        string WineName, string Producer, string Type,
        string? Country, decimal? Rating, string? Notes, DateTime? TastedAt);

    private record ExpertMessageRow(
        Guid Id, string Role, string Content, string? ModelUsed, DateTime CreatedAt);

    private record ExpertWineSuggestionRow(
        Guid Id, Guid MessageId, Guid? WineId, string WineDataJson, short? Feedback);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "jeg", "har", "kan", "vil", "til", "den", "det", "som", "med", "for",
        "meg", "deg", "oss", "hva", "noe", "noen", "fra", "ikke", "men", "eller",
        "vin", "viner", "fin", "fint", "god", "godt", "bra", "anbefal", "anbefaling",
        "finn", "foreslå", "lignende", "liker", "likt", "prøv", "prøvd",
        "the", "and", "for", "that", "with",
    };
}

/// <summary>Thrown when the user has exceeded their daily expert ask quota.</summary>
public sealed class ExpertQuotaExceededException : Exception
{
    public int ScansToday { get; }
    public int DailyLimit { get; }
    public int ScansRemaining { get; }

    public ExpertQuotaExceededException(int scansToday, int dailyLimit, int scansRemaining)
        : base("Daglig kvote for AI-forespørsler er nådd.")
    {
        ScansToday     = scansToday;
        DailyLimit     = dailyLimit;
        ScansRemaining = scansRemaining;
    }
}
