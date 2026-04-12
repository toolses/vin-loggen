namespace VinLoggen.Api.Models;

/// <summary>
/// Cached Google Places result.  Maps to the <c>locations</c> table.
/// </summary>
public record LocationRecord(
    Guid      Id,
    string    GooglePlaceId,  // mapped from google_place_id
    string    Name,
    string?   Address,
    double    Lat,
    double    Lng,
    string[]? Types,
    DateTime  CreatedAt       // mapped from created_at
);
