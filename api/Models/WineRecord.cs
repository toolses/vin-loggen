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

/// <summary>Lightweight projection used by the user-facing wine search endpoint.</summary>
public record WineSearchResult(
    Guid    Id,
    string  Name,
    string  Producer,
    int?    Vintage,
    string  Type,
    string? Country,
    string? Region,
    string[]? Grapes,
    decimal?  AlcoholContent
);

// ── Smart save DTOs ─────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/wines/save.
/// Contains the user's current field values, the original AI values for comparison,
/// and the tasting log data.
/// </summary>
public record WineSaveRequest(
    // Current master data (what the user sees/edited)
    string    Name,
    string    Producer,
    int?      Vintage,
    string    Type,
    string?   Country,
    string?   Region,
    string[]? Grapes,
    double?   AlcoholContent,
    string?   ExternalSourceId,
    string[]? FoodPairings,
    string?   Description,
    string?   TechnicalNotes,
    // Original AI/API data (for correction detection)
    WineOriginalData? OriginalData,
    // Existing wine reference (from dedup/search)
    Guid?     ExistingWineId,
    // Log data
    decimal?  Rating,
    string?   Notes,
    string?   ImageUrl,
    string?   ThumbnailUrl,
    string?   TastedAt,
    string?   LocationName,
    double?   LocationLat,
    double?   LocationLng,
    string?   LocationType
);

/// <summary>
/// Snapshot of the AI/API-provided values at scan time.
/// Sent alongside the user's current values so the backend can detect corrections.
/// </summary>
public record WineOriginalData(
    string?   Name,
    string?   Producer,
    int?      Vintage,
    string?   Type,
    string?   Country,
    string?   Region,
    string[]? Grapes,
    double?   AlcoholContent,
    string    Source  // "gemini" | "wineapi"
);

/// <summary>Response from POST /api/wines/save.</summary>
public record WineSaveResponse(
    Guid   WineId,
    Guid   LogId,
    bool   CorrectionLogged,
    bool   NewWineCreated
);

/// <summary>Request body for POST /api/wines/{id}/report.</summary>
public record WineReportRequest(
    string   Comment,
    string?  FieldName
);
