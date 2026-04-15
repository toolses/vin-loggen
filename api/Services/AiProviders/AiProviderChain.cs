namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// Resolves and iterates through the configured priority list of AI providers,
/// returning the first successful result or falling back to the next on transient failure.
/// </summary>
public sealed class AiProviderChain
{
    private readonly IEnumerable<IAiChatProvider> _chatProviders;
    private readonly GroqTokenBudgetService _groqBudget;
    private readonly ILogger<AiProviderChain> _logger;

    public AiProviderChain(
        IEnumerable<IAiChatProvider> chatProviders,
        GroqTokenBudgetService groqBudget,
        ILogger<AiProviderChain> logger)
    {
        _chatProviders = chatProviders;
        _groqBudget = groqBudget;
        _logger = logger;
    }

    /// <summary>
    /// Runs through the chat provider priority list. On transient failure from
    /// any provider, the next provider in the list is tried.
    /// </summary>
    public async Task<AiChatResult> ChatAsync(
        string[] priority, string systemPrompt, string userContent, CancellationToken ct,
        Guid? userId        = null,
        Guid? correlationId = null)
    {
        var providers = await ResolveChatAsync(priority, ct);

        foreach (var provider in providers)
        {
            var result = await provider.ChatAsync(systemPrompt, userContent, ct, userId, correlationId);

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

    private async Task<List<IAiChatProvider>> ResolveChatAsync(string[] priority, CancellationToken ct)
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

            // Proactively skip Groq when the sliding-window token budget is exhausted
            if (name.Equals("Groq", StringComparison.OrdinalIgnoreCase)
                && !await _groqBudget.HasBudgetAsync(ct))
            {
                _logger.LogInformation("AiProviderChain: skipping Groq — TPM budget exhausted, falling back");
                continue;
            }

            ordered.Add(provider);
        }
        return ordered;
    }
}
