using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using VinLoggen.Api.Configuration;
using VinLoggen.Api.Services;
using Xunit;

namespace VinLoggen.Api.Tests.Services;

/// <summary>
/// Unit tests for WineOrchestratorService using mocked dependencies.
/// Tests that require a real database use userId = null so the dedup
/// and quota code paths are skipped.
/// </summary>
public class WineOrchestratorServiceTests : IDisposable
{
    private static readonly byte[] FakeImage = [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG magic bytes
    private const string Jpeg = "image/jpeg";

    private readonly Mock<IGeminiService>   _gemini   = new();
    private readonly Mock<IWineApiService>  _wineApi  = new();
    private readonly Mock<IProUsageService> _proUsage = new();
    private readonly NpgsqlDataSource       _dataSource;
    private readonly WineOrchestratorService _sut;

    private static readonly WineAnalysisResponse DefaultAnalysis = new(
        WineName:       "Barolo Riserva",
        Producer:       "Marchesi di Barolo",
        Vintage:        2018,
        Country:        "Italia",
        Region:         "Piemonte",
        Grapes:         ["Nebbiolo"],
        Type:           "Rød",
        AlcoholContent: 14.5
    );

    public WineOrchestratorServiceTests()
    {
        // A dummy data-source that won't be contacted in null-userId tests
        _dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=5999;Database=test_placeholder");

        _sut = new WineOrchestratorService(
            _gemini.Object,
            _wineApi.Object,
            _proUsage.Object,
            _dataSource,
            new IntegrationSettings { EnableGemini = true, EnableWineApi = true, DailyProLimit = 10 },
            NullLogger<WineOrchestratorService>.Instance);
    }

    public void Dispose() => _dataSource.Dispose();

    // ── Unauthenticated path (userId = null) ──────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_NullUserId_ReturnsBasicOcrResult()
    {
        _gemini.Setup(g => g.AnalyzeLabelAsync(FakeImage, Jpeg, default))
               .ReturnsAsync(new GeminiResult<WineAnalysisResponse>(DefaultAnalysis, null));

        var result = await _sut.AnalyzeAsync(FakeImage, Jpeg, userId: null, CancellationToken.None);

        Assert.Equal("Barolo Riserva", result.WineName);
        Assert.Equal("Marchesi di Barolo", result.Producer);
        Assert.Equal(2018, result.Vintage);
        Assert.False(result.AlreadyTasted);
        Assert.Null(result.FoodPairings);
        Assert.False(result.ProLimitReached);
        Assert.Equal(0, result.ProScansToday);
    }

    [Fact]
    public async Task AnalyzeAsync_NullUserId_NeverCallsProUsageOrWineApi()
    {
        _gemini.Setup(g => g.AnalyzeLabelAsync(FakeImage, Jpeg, default))
               .ReturnsAsync(new GeminiResult<WineAnalysisResponse>(DefaultAnalysis, null));

        await _sut.AnalyzeAsync(FakeImage, Jpeg, userId: null, CancellationToken.None);

        _proUsage.VerifyNoOtherCalls();
        _wineApi.VerifyNoOtherCalls();
    }

    // ── Gemini disabled ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_GeminiDisabled_ReturnsEmptyResult()
    {
        var sut = new WineOrchestratorService(
            _gemini.Object, _wineApi.Object, _proUsage.Object, _dataSource,
            new IntegrationSettings { EnableGemini = false },
            NullLogger<WineOrchestratorService>.Instance);

        var result = await sut.AnalyzeAsync(FakeImage, Jpeg, userId: null, CancellationToken.None);

        Assert.Null(result.WineName);
        _gemini.VerifyNoOtherCalls();
    }

    // ── Gemini failure ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_GeminiReturnsError_ThrowsInvalidOperation()
    {
        _gemini.Setup(g => g.AnalyzeLabelAsync(FakeImage, Jpeg, default))
               .ReturnsAsync(new GeminiResult<WineAnalysisResponse>(null, "API quota exceeded"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AnalyzeAsync(FakeImage, Jpeg, userId: null, CancellationToken.None));
    }

    // ── Authenticated path (quota + enrichment) ───────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_AuthenticatedWithQuota_CallsWineApiAndReturnsFoodPairings()
    {
        var userId = Guid.NewGuid();

        _gemini.Setup(g => g.AnalyzeLabelAsync(FakeImage, Jpeg, default))
               .ReturnsAsync(new GeminiResult<WineAnalysisResponse>(DefaultAnalysis, null));

        _proUsage.Setup(p => p.GetStatusAsync(userId, default))
                 .ReturnsAsync(new ProUsageService.ProStatus(
                     CanUsePro: true, IsPro: false, ScansToday: 3, DailyLimit: 10, ScansRemaining: 7));

        var enrichment = new WineApiService.WineEnrichment(
            ExternalId:     "api-001",
            Description:    "A classic Barolo with rich tannins.",
            FoodPairings:   ["Lammekoteletter", "Modnet ost", "Biff"],
            TechnicalNotes: "Intens rubinrød farge.",
            AlcoholContent: 14.5,
            Grapes:         ["Nebbiolo"]);

        _wineApi.Setup(w => w.FindAsync("Marchesi di Barolo", "Barolo Riserva", 2018, default))
                .ReturnsAsync(enrichment);

        _proUsage.Setup(p => p.IncrementAsync(userId, default)).Returns(Task.CompletedTask);

        // Refresh after increment
        _proUsage.Setup(p => p.GetStatusAsync(userId, default))
                 .ReturnsAsync(new ProUsageService.ProStatus(
                     CanUsePro: true, IsPro: false, ScansToday: 4, DailyLimit: 10, ScansRemaining: 6));

        var result = await _sut.AnalyzeAsync(FakeImage, Jpeg, userId, CancellationToken.None);

        Assert.NotNull(result.FoodPairings);
        Assert.Equal(3, result.FoodPairings!.Length);
        Assert.Equal("api-001", result.ExternalSourceId);
        Assert.False(result.ProLimitReached);
    }

    [Fact]
    public async Task AnalyzeAsync_QuotaExhausted_SkipsEnrichmentAndSetsProLimitReached()
    {
        var userId = Guid.NewGuid();

        _gemini.Setup(g => g.AnalyzeLabelAsync(FakeImage, Jpeg, default))
               .ReturnsAsync(new GeminiResult<WineAnalysisResponse>(DefaultAnalysis, null));

        _proUsage.Setup(p => p.GetStatusAsync(userId, default))
                 .ReturnsAsync(new ProUsageService.ProStatus(
                     CanUsePro: false, IsPro: false, ScansToday: 10, DailyLimit: 10, ScansRemaining: 0));

        var result = await _sut.AnalyzeAsync(FakeImage, Jpeg, userId, CancellationToken.None);

        Assert.True(result.ProLimitReached);
        Assert.Null(result.FoodPairings);
        _wineApi.VerifyNoOtherCalls();
    }
}
