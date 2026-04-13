namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// Result from an AI vision (label scan) call — includes the raw JSON and provider name.
/// </summary>
public record AiVisionResult(string? RawJson, string ProviderName, bool IsSuccess)
{
    public bool IsTransient { get; init; }
}

/// <summary>
/// Abstraction for a vision-based AI provider that can analyse wine label images.
/// </summary>
public interface IAiVisionProvider
{
    string Name { get; }
    bool IsAvailable { get; }

    Task<AiVisionResult> AnalyzeImageAsync(
        string systemPrompt, byte[] imageBytes, string mimeType, CancellationToken ct);

    Task<AiVisionResult> AnalyzeImagesAsync(
        string systemPrompt,
        byte[] frontImageBytes, string frontMimeType,
        byte[]? backImageBytes, string? backMimeType,
        CancellationToken ct);
}
