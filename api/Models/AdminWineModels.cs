namespace VinLoggen.Api.Models;

public record AdminWineListItem(
    Guid     Id,
    string   Name,
    string   Producer,
    int?     Vintage,
    string   Type,
    string?  Country,
    string?  Region,
    int      LogCount,
    DateTime CreatedAt
);

public sealed class AdminWineDetail
{
    public Guid      Id               { get; set; }
    public string    Name             { get; set; } = "";
    public string    Producer         { get; set; } = "";
    public int?      Vintage          { get; set; }
    public string    Type             { get; set; } = "";
    public string?   Country          { get; set; }
    public string?   Region           { get; set; }
    public string[]? Grapes           { get; set; }
    public decimal?  AlcoholContent   { get; set; }
    public string?   ExternalSourceId { get; set; }
    public string[]? FoodPairings     { get; set; }
    public string?   Description      { get; set; }
    public string?   TechnicalNotes   { get; set; }
    public int       LogCount         { get; set; }
    public DateTime  CreatedAt        { get; set; }
}

public record AdminWineUpdateRequest(
    string    Name,
    string    Producer,
    int?      Vintage,
    string    Type,
    string?   Country,
    string?   Region,
    string[]? Grapes,
    double?   AlcoholContent,
    string[]? FoodPairings,
    string?   Description,
    string?   TechnicalNotes
);

public record PaginatedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
