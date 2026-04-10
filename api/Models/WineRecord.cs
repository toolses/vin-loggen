namespace VinLoggen.Api.Models;

/// <summary>
/// Maps to the <c>wines</c> table in Supabase (PostgreSQL).
/// Column names use snake_case; Dapper maps them via the SQL alias in queries.
/// </summary>
public record WineRecord(
    Guid Id,
    string Name,
    string Producer,
    int? Vintage,
    string Type,
    string? Country,
    string? Region,
    decimal? Rating,
    string? Notes,
    string? ImageUrl,     // mapped from image_url
    DateTime CreatedAt    // mapped from created_at
);
