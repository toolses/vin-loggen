namespace VinLoggen.Api.Models;

/// <summary>
/// One user tasting-event.  Maps to the <c>wine_logs</c> table.
/// </summary>
public record WineLogRecord(
    Guid      Id,
    Guid      WineId,        // mapped from wine_id
    Guid?     UserId,        // mapped from user_id
    decimal?  Rating,
    string?   Notes,
    string?   ImageUrl,      // mapped from image_url
    DateOnly? TastedAt,      // mapped from tasted_at
    string?   LocationName,  // mapped from location_name
    double?   LocationLat,   // mapped from location_lat
    double?   LocationLng,   // mapped from location_lng
    string?   LocationType,  // mapped from location_type
    DateTime  CreatedAt      // mapped from created_at
);
