namespace VinLoggen.Api.Services.AiProviders;

/// <summary>
/// Result from an AI chat completion — includes the provider name for observability.
/// </summary>
public record AiChatResult(string? Answer, string ProviderName, bool IsSuccess)
{
    /// <summary>True when the failure is transient (503, 429, timeout) and the next provider should be tried.</summary>
    public bool IsTransient { get; init; }
}

/// <summary>
/// Abstraction for a text-based AI chat provider (DeepSeek, Gemini, Groq, etc.).
/// </summary>
public interface IAiChatProvider
{
    /// <summary>Short provider name used in logs and the admin dashboard (e.g. "DeepSeek", "Gemini").</summary>
    string Name { get; }

    /// <summary>Returns true when the provider has valid configuration (API key present, enabled, etc.).</summary>
    bool IsAvailable { get; }

    /// <summary>Send a chat completion request with a system instruction and user context.</summary>
    Task<AiChatResult> ChatAsync(string systemPrompt, string userContent, CancellationToken ct);
}
