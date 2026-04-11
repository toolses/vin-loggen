namespace VinLoggen.Api.Models;

public record UserProfile(
    Guid UserId,
    string? TasteProfileJson,
    int WinesAtLastAnalysis,
    DateTime? LastAnalysisAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
