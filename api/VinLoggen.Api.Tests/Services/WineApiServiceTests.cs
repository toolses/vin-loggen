using VinLoggen.Api.Services;
using Xunit;

namespace VinLoggen.Api.Tests.Services;

/// <summary>
/// Tests for the pure scoring/matching logic inside WineApiService.
/// These access internal members via InternalsVisibleTo.
/// </summary>
public class WineApiServiceTests
{
    // ── Score ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_BothProducerAndNameSubstringMatch_ReturnsFour()
    {
        var hit = MakeHit(producer: "Marchesi di Barolo", name: "Barolo Riserva", vintage: null);
        var score = WineApiService.Score(hit, "marchesi di barolo", "barolo riserva", null);
        Assert.Equal(4, score);
    }

    [Fact]
    public void Score_ExactMatchPlusVintage_ReturnsFive()
    {
        var hit = MakeHit(producer: "Marchesi di Barolo", name: "Barolo Riserva", vintage: 2018);
        var score = WineApiService.Score(hit, "marchesi di barolo", "barolo riserva", 2018);
        Assert.Equal(5, score);
    }

    [Fact]
    public void Score_NameContainsQuery_ReturnsTwo()
    {
        // hit name is longer but contains the query substring
        var hit = MakeHit(producer: "Other", name: "Barolo Riserva DOCG", vintage: null);
        var score = WineApiService.Score(hit, "other", "barolo riserva", null);
        Assert.Equal(4, score); // name contains (2) + producer contains (2)
    }

    [Fact]
    public void Score_NoSubstringMatch_ReturnsZero()
    {
        var hit = MakeHit(producer: "Antinori", name: "Tignanello", vintage: 2020);
        var score = WineApiService.Score(hit, "sassicaia", "bolgheri", 2020);
        Assert.Equal(0, score); // vintage match doesn't count without name/producer match
    }

    [Fact]
    public void Score_VintageMismatch_DoesNotAddPoint()
    {
        var hit = MakeHit(producer: "Antinori", name: "Tignanello", vintage: 2019);
        var score = WineApiService.Score(hit, "antinori", "tignanello", 2020);
        Assert.Equal(4, score); // producer+name match but vintage doesn't match
    }

    [Fact]
    public void Score_NullVintageOnBothSides_DoesNotAddPoint()
    {
        var hit = MakeHit(producer: "Antinori", name: "Tignanello", vintage: null);
        var score = WineApiService.Score(hit, "antinori", "tignanello", null);
        Assert.Equal(4, score); // null != null for vintage bonus
    }

    // ── FindBestMatch ─────────────────────────────────────────────────────────

    [Fact]
    public void FindBestMatch_NullList_ReturnsNull()
    {
        var result = WineApiService.FindBestMatch(null, "producer", "name", null);
        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_EmptyList_ReturnsNull()
    {
        var result = WineApiService.FindBestMatch([], "producer", "name", null);
        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_SingleExactMatch_ReturnsThatHit()
    {
        var hit = MakeHit(producer: "Sassicaia", name: "Sassicaia DOC Bolgheri", vintage: 2019, id: "sa-1");
        var result = WineApiService.FindBestMatch([hit], "sassicaia", "sassicaia doc bolgheri", 2019);
        Assert.Equal("sa-1", result?.Id);
    }

    [Fact]
    public void FindBestMatch_MultipleHits_ReturnsBestScoring()
    {
        var weak   = MakeHit(producer: "Other",    name: "Bolgheri",               vintage: 2019, id: "weak");
        var strong = MakeHit(producer: "Sassicaia", name: "Sassicaia DOC Bolgheri", vintage: 2019, id: "strong");
        var result = WineApiService.FindBestMatch([weak, strong], "sassicaia", "sassicaia doc bolgheri", 2019);
        Assert.Equal("strong", result?.Id);
    }

    [Fact]
    public void FindBestMatch_NoMatchingHit_ReturnsNull()
    {
        var hit = MakeHit(producer: "Antinori", name: "Tignanello", vintage: 2020, id: "x");
        // Query is completely different → score 0 → filtered out
        var result = WineApiService.FindBestMatch([hit], "unrelated", "wine", null);
        Assert.Null(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WineApiService.WineApiHit MakeHit(
        string? producer, string? name, int? vintage, string? id = null) =>
        new(
            Id:             id ?? "test-id",
            Name:           name,
            Producer:       producer,
            Vintage:        vintage,
            Description:    null,
            FoodPairing:    null,
            FoodPairings:   null,
            TechnicalNotes: null,
            AlcoholContent: null,
            Grapes:         null
        );
}
