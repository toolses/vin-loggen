namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// Resolves and iterates through the configured priority list of AI providers,
/// returning the first successful result or falling back to the next on transient failure.
/// </summary>
public sealed class AiProviderChain
{
    private readonly IEnumerable<IAiChatProvider> _chatProviders;
    private readonly IEnumerable<IAiVisionProvider> _visionProviders;
    private readonly ILogger<AiProviderChain> _logger;

    public AiProviderChain(
        IEnumerable<IAiChatProvider> chatProviders,
        IEnumerable<IAiVisionProvider> visionProviders,
        ILogger<AiProviderChain> logger)
    {
        _chatProviders = chatProviders;
        _visionProviders = visionProviders;
        _logger = logger;
    }

    /// <summary>
    /// Runs through the chat provider priority list. On transient failure from
    /// any provider, the next provider in the list is tried.
    /// </summary>
    public async Task<AiChatResult> ChatAsync(
        string[] priority, string systemPrompt, string userContent, CancellationToken ct)
    {
        var providers = ResolveChat(priority);

        foreach (var provider in providers)
        {
            var result = await provider.ChatAsync(systemPrompt, userContent, ct);

            if (result.IsSuccess)
                return result;

            if (result.IsTransient)
            {
                _logger.LogWarning("AiProviderChain: {Provider} returned transient failure, trying next provider", provider.Name);
                continue;
            }

            // Non-transient failure — still try the next provider
            _logger.LogWarning("AiProviderChain: {Provider} returned non-transient failure, trying next provider", provider.Name);
        }

        _logger.LogError("AiProviderChain: all chat providers exhausted");
        return new AiChatResult(null, "none", false);
    }

    /// <summary>
    /// Runs through the vision provider priority list with fallback.
    /// </summary>
    public async Task<AiVisionResult> AnalyzeImageAsync(
        string[] priority, string systemPrompt, byte[] imageBytes, string mimeType, CancellationToken ct)
    {
        var providers = ResolveVision(priority);

        foreach (var provider in providers)
        {
            var result = await provider.AnalyzeImageAsync(systemPrompt, imageBytes, mimeType, ct);
            if (result.IsSuccess) return result;

            _logger.LogWarning("AiProviderChain: vision provider {Provider} failed, trying next", provider.Name);
        }

        return new AiVisionResult(null, "none", false);
    }

    public async Task<AiVisionResult> AnalyzeImagesAsync(
        string[] priority, string systemPrompt,
        byte[] frontBytes, string frontMime, byte[]? backBytes, string? backMime,
        CancellationToken ct)
    {
        var providers = ResolveVision(priority);

        foreach (var provider in providers)
        {
            var result = await provider.AnalyzeImagesAsync(systemPrompt, frontBytes, frontMime, backBytes, backMime, ct);
            if (result.IsSuccess) return result;

            _logger.LogWarning("AiProviderChain: vision provider {Provider} failed (multi), trying next", provider.Name);
        }

        return new AiVisionResult(null, "none", false);
    }

    private List<IAiChatProvider> ResolveChat(string[] priority)
    {
        var ordered = new List<IAiChatProvider>();
        foreach (var name in priority)
        {
            var provider = _chatProviders.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (provider is null)
            {
                _logger.LogDebug("AiProviderChain: chat provider '{Name}' not registered, skipping", name);
                continue;
            }

            if (!provider.IsAvailable)
            {
                _logger.LogDebug("AiProviderChain: chat provider '{Name}' not available (no API key?), skipping", name);
                continue;
            }

            ordered.Add(provider);
        }
        return ordered;
    }

    private List<IAiVisionProvider> ResolveVision(string[] priority)
    {
        var ordered = new List<IAiVisionProvider>();
        foreach (var name in priority)
        {
            var provider = _visionProviders.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (provider is null || !provider.IsAvailable)
                continue;

            ordered.Add(provider);
        }
        return ordered;
    }
}
