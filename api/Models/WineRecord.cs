namespace VinLoggen.Api.Models;

/// <summary>
/// Master/catalogue data for a wine.  Maps to the <c>wines</c> table.
/// User-specific tasting data lives in <c>wine_logs</c> (<see cref="WineLogRecord"/>).
/// </summary>
public record WineRecord(
    Guid      Id,
    string    Name,
    string    Producer,
    int?      Vintage,
    string    Type,
    string?   Country,
    string?   Region,
    string[]? Grapes,           // mapped from grapes (TEXT[])
    double?   AlcoholContent,   // mapped from alcohol_content
    string?   ExternalSourceId, // mapped from external_source_id
    DateTime  CreatedAt         // mapped from created_at
);
