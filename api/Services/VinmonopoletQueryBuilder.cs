using System.Text.RegularExpressions;

namespace VinLoggen.Api.Services;

/// <summary>
/// Builds Vinmonopolet search URLs from structured wine type parameters.
/// Query format: <c>?q=:relevance:param1:value1:param2:value2</c>
/// </summary>
public static partial class VinmonopoletQueryBuilder
{
    private const string BaseUrl = "https://www.vinmonopolet.no/search";

    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rødvin"]      = "rødvin",
        ["Rød"]         = "rødvin",
        ["Hvitvin"]     = "hvitvin",
        ["Hvit"]        = "hvitvin",
        ["Rosévin"]     = "rosévin",
        ["Rosé"]        = "rosévin",
        ["Musserende"]  = "musserende_vin",
        ["Oransje"]     = "hvitvin",
        // Dessert type is handled by dessertCategory field from AI
    };

    /// <summary>Known dessert/fortified category values the AI can return.</summary>
    private static readonly HashSet<string> ValidDessertCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "sterkvin", "dessertvin",
    };

    private static readonly HashSet<string> ValidFoodCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "L", "N", "Q", "R",
    };

    private static readonly HashSet<string> ValidRanges =
    [
        "1-2", "3-4", "5-6", "7-8", "9-10", "11-12",
    ];

    /// <summary>
    /// Build a Vinmonopolet search URL from structured wine-type parameters.
    /// Invalid values are silently skipped (lenient parsing).
    /// </summary>
    public static string BuildSearchUrl(
        string? category,
        string? dessertCategory,
        string? country,
        string? grape,
        SearchHints? hints)
    {
        var parts = new List<string> { "relevance" };

        // Category
        if (category is not null)
        {
            if (category.Equals("Dessert", StringComparison.OrdinalIgnoreCase)
                || category.Equals("Dessertvin", StringComparison.OrdinalIgnoreCase)
                || category.Equals("Sterkvin", StringComparison.OrdinalIgnoreCase))
            {
                // AI picks the specific dessert category
                var dc = dessertCategory ?? "sterkvin";
                if (ValidDessertCategories.Contains(dc))
                    parts.Add($"mainCategory:{dc}");
            }
            else if (CategoryMap.TryGetValue(category, out var mainCat))
            {
                parts.Add($"mainCategory:{mainCat}");
            }
        }

        // Country
        if (!string.IsNullOrWhiteSpace(country))
            parts.Add($"mainCountry:{country.ToLowerInvariant()}");

        // Grape
        if (!string.IsNullOrWhiteSpace(grape))
            parts.Add($"Raastoff:{grape}");

        if (hints is not null)
        {
            // Food pairing codes — disabled for now (over-filters results on Vinmonopolet)
            // if (hints.IsGoodFor is { Length: > 0 })
            // {
            //     foreach (var code in hints.IsGoodFor)
            //     {
            //         if (ValidFoodCodes.Contains(code))
            //             parts.Add($"isGoodfor:{code}");
            //     }
            // }

            // Characteristics (lenient — skip invalid ranges)
            TryAddRange(parts, "Fylde", hints.Fylde);
            TryAddRange(parts, "Friskhet", hints.Friskhet);
            TryAddRange(parts, "Soedme", hints.Soedme);
            TryAddRange(parts, "Bitterhet", hints.Bitterhet);
        }

        var query = string.Join(":", parts.Select(Uri.EscapeDataString));
        // The Vinmonopolet format uses %3A as separator (URL-encoded colon)
        // but the initial colon before "relevance" is literal
        return $"{BaseUrl}?q=%3A{string.Join("%3A", parts)}";
    }

    /// <summary>
    /// Build a Google search URL for a wine type.
    /// </summary>
    public static string BuildGoogleSearchUrl(
        string? subType, string? region, string? country, string? category)
    {
        var terms = new[] { subType, region, country, category, "vin" }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        var query = Uri.EscapeDataString(string.Join(" ", terms));
        return $"https://www.google.com/search?q={query}";
    }

    private static void TryAddRange(List<string> parts, string paramName, string? range)
    {
        if (range is not null && ValidRanges.Contains(range))
            parts.Add($"{paramName}:{range}");
    }
}

/// <summary>
/// Structured search hints from the AI for building Vinmonopolet queries.
/// All fields are optional — lenient parsing skips invalid values.
/// </summary>
public record SearchHints(
    string[]? IsGoodFor = null,
    string? Fylde       = null,
    string? Friskhet    = null,
    string? Soedme      = null,
    string? Bitterhet   = null
);
