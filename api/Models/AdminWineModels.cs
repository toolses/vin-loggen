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

public record AdminWineDetail(
    Guid      Id,
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
    int       LogCount,
    DateTime  CreatedAt
);

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
