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
}
