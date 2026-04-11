namespace VinLoggen.Api.Services;

/// <summary>
/// Abstraction over the Gemini AI service – enables mocking in unit tests.
/// </summary>
public interface IGeminiService
{
    Task<GeminiResult<WineAnalysisResponse>> AnalyzeLabelAsync(
        byte[]            imageBytes,
        string            mimeType,
        CancellationToken ct);

    Task<GeminiResult<WineAnalysisResponse>> AnalyzeLabelsAsync(
        byte[]            frontImageBytes,
        string            frontMimeType,
        byte[]?           backImageBytes,
        string?           backMimeType,
        CancellationToken ct);

    Task<GeminiService.FoodPairingResult?> GetFoodPairingsAsync(
        string? wineName,
        string? producer,
        int?    vintage,
        string? type,
        string? country,
        CancellationToken ct);

    Task<TasteProfileResponse?> GenerateTasteProfileAsync(
        IEnumerable<WineProfileData> wines,
        CancellationToken ct);
}
