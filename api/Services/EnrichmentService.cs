namespace VinLoggen.Api.Services;

/// <summary>
/// Stub enrichment service – intended to call external wine databases
/// (e.g. wineapi.io) to fill in missing master-data fields and obtain
/// a stable <c>external_source_id</c> for de-duplication across sources.
///
/// Wire up a real HTTP client here when an API key is available.
/// The <c>external_source_id</c> column in <c>wines</c> is reserved for
/// the identifier returned by the chosen external catalogue.
/// </summary>
public sealed class EnrichmentService
{
    private readonly ILogger<EnrichmentService> _logger;

    public EnrichmentService(ILogger<EnrichmentService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to look up a wine by producer + name + vintage in an
    /// external catalogue and returns a canonical source ID.
    /// Returns <c>null</c> when no match is found or the service is
    /// not yet configured.
    /// </summary>
    public Task<string?> FindExternalIdAsync(
        string  producer,
        string  name,
        int?    vintage,
        CancellationToken ct)
    {
        // TODO: implement when wineapi.io (or similar) credentials are available.
        _logger.LogDebug(
            "EnrichmentService.FindExternalIdAsync called for '{Producer}' / '{Name}' / {Vintage} – stub, returning null",
            producer, name, vintage);

        return Task.FromResult<string?>(null);
    }
}
