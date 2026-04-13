using VinLoggen.Api.Models;

namespace VinLoggen.Api.Services;

public record ExpertRequest(string Question, Guid? SessionId = null);

public record ExpertWineReference(
    Guid    Id,
    string  Name,
    string  Producer,
    int?    Vintage,
    string  Type,
    string? Country,
    string?   Region         = null,
    string[]? FoodPairings   = null,
    string?   Description    = null,
    string?   TechnicalNotes = null,
    string?   WhyRecommended = null,
    string?   Source         = null   // "catalog" | "wineapi" | "ai"
);

public record ExpertResponse(
    string                  Answer,
    ExpertWineReference[]?  ReferencedWines,
    int                     ProScansToday,
    int                     DailyProLimit,
    int                     ScansRemaining,
    string?                 ModelUsed          = null,
    Guid?                   SessionId          = null,
    Guid[]?                 WineSuggestionIds  = null
);

public interface IExpertService
{
    Task<ExpertResponse> AskAsync(Guid userId, ExpertRequest request, CancellationToken ct);

    Task<IEnumerable<ExpertSessionSummary>> GetSessionsAsync(Guid userId, int limit, int offset, CancellationToken ct);

    Task<ExpertSessionDetail?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);

    Task<bool> SetWineFeedbackAsync(Guid userId, Guid suggestionId, short feedback, CancellationToken ct);

    Task<bool> DeleteSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);
}
