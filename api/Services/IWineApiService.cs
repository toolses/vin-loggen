namespace VinLoggen.Api.Services;

/// <summary>
/// Abstraction over the wineapi.io client – enables mocking in unit tests.
/// </summary>
public interface IWineApiService
{
    Task<WineApiService.WineEnrichment?> FindAsync(
        string  producer,
        string  name,
        int?    vintage,
        CancellationToken ct,
        Guid?   userId        = null,
        Guid?   correlationId = null);

    /// <summary>
    /// Identifies a wine from a free-text description via /identify/text.
    /// Returns a matched wine profile or null if no match / feature disabled.
    /// </summary>
    Task<WineApiService.WineIdentification?> IdentifyByTextAsync(
        string query,
        CancellationToken ct,
        Guid?  userId        = null,
        Guid?  correlationId = null);

    /// <summary>
    /// Fetches full wine details from GET /wines/{id}.
    /// Returns enrichment data (alcohol, grapes, description, food pairings, etc.)
    /// or null if the feature is disabled, no API key, or the call fails.
    /// </summary>
    Task<WineApiService.WineEnrichment?> GetDetailsAsync(
        string wineId,
        CancellationToken ct,
        Guid?  userId        = null,
        Guid?  correlationId = null);

    // ── Admin test methods (return raw WineAPI data, skip quota) ─────────

    Task<List<WineApiSearchHitDto>?> SearchRawAsync(
        string producer, string name, int? vintage,
        CancellationToken ct,
        Guid? userId = null, Guid? correlationId = null);

    Task<WineApiDetailDto?> GetDetailsRawAsync(
        string wineId,
        CancellationToken ct,
        Guid? userId = null, Guid? correlationId = null);

    Task<WineApiIdentifyResultDto?> IdentifyByTextRawAsync(
        string query,
        CancellationToken ct,
        Guid? userId = null, Guid? correlationId = null);
}

// ── Public DTOs for admin test endpoints ──────────────────────────────────

public record WineApiSearchHitDto(
    string? Id, string? Name, string? Winery, int? Vintage,
    string? Type, string? Region, string? Country,
    double? AverageRating, int? RatingsCount, double? Confidence);

public record WineApiDetailDto(
    string? Id, string? Name, string? Winery, int? Vintage,
    string? Type, string? Region, string? Country, string? Description,
    string[]? FoodPairings, string? TechnicalNotes, double? AlcoholContent,
    string[]? Grapes, double? AverageRating, int? RatingsCount);

public record WineApiIdentifyHitDto(
    string? Id, string? Name, int? Vintage,
    string? Type, string? Region, string? Country,
    double? AverageRating, int? RatingsCount);

public record WineApiIdentifyResultDto(
    WineApiIdentifyHitDto? Wine,
    List<WineApiIdentifyHitDto>? Suggestions,
    double? Confidence);
