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
        CancellationToken ct);

    /// <summary>
    /// Identifies a wine from a free-text description via /identify/text.
    /// Returns a matched wine profile or null if no match / feature disabled.
    /// </summary>
    Task<WineApiService.WineIdentification?> IdentifyByTextAsync(
        string query,
        CancellationToken ct);

    /// <summary>
    /// Fetches full wine details from GET /wines/{id}.
    /// Returns enrichment data (alcohol, grapes, description, food pairings, etc.)
    /// or null if the feature is disabled, no API key, or the call fails.
    /// </summary>
    Task<WineApiService.WineEnrichment?> GetDetailsAsync(
        string wineId,
        CancellationToken ct);
}
