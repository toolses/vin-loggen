namespace VinLoggen.Api.Models;

public record AdminUserListItem(
    Guid     Id,
    string?  Email,
    string?  DisplayName,
    string   SubscriptionTier,
    int      ProScansToday,
    bool     IsAdmin,
    DateTime CreatedAt
);

public record AdminUserTierUpdateRequest(string SubscriptionTier);
