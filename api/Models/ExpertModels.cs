namespace VinLoggen.Api.Models;

// ── Session list item (for history) ─────────────────────────────────────────

public record ExpertSessionSummary(
    Guid     Id,
    string?  Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int      MessageCount
);

// ── Full session with messages ──────────────────────────────────────────────

public record ExpertSessionDetail(
    Guid                Id,
    string?             Title,
    DateTime            CreatedAt,
    ExpertMessageDto[]  Messages
);

public record ExpertMessageDto(
    Guid                        Id,
    string                      Role,
    string                      Content,
    string?                     ModelUsed,
    DateTime                    CreatedAt,
    ExpertWineSuggestionDto[]?  Wines
);

public record ExpertWineSuggestionDto(
    Guid    Id,
    Guid?   WineId,
    string  WineDataJson,
    short?  Feedback
);

// ── Feedback request ────────────────────────────────────────────────────────

public record WineSuggestionFeedbackRequest(short Feedback);
