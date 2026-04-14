namespace VinLoggen.Api.Services;

public interface IAppSettingsService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
