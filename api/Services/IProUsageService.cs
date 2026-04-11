namespace VinLoggen.Api.Services;

/// <summary>
/// Abstraction over the Pro quota service – enables mocking in unit tests.
/// </summary>
public interface IProUsageService
{
    Task<ProUsageService.ProStatus> GetStatusAsync(Guid userId, CancellationToken ct);
    Task IncrementAsync(Guid userId, CancellationToken ct);
}
