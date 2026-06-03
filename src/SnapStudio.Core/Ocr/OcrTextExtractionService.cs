using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Ocr;

public sealed class OcrTextExtractionService : IOcrTextExtractionService
{
    private const double RegionTolerance = 0.1;
    private readonly IOcrProvider _ocrProvider;
    private readonly IOcrResultCache _resultCache;

    public OcrTextExtractionService(
        IOcrProvider ocrProvider,
        IOcrResultCache resultCache)
    {
        ArgumentNullException.ThrowIfNull(ocrProvider);
        ArgumentNullException.ThrowIfNull(resultCache);

        _ocrProvider = ocrProvider;
        _resultCache = resultCache;
    }

    public async Task<OcrTextExtractionResult> ExtractAsync(
        OcrTextExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        OcrCacheDocument cacheDocument = await _resultCache
            .GetAsync(request.DocumentId, cancellationToken)
            .ConfigureAwait(false);
        OcrResult? cachedResult = cacheDocument
            .Results
            .FirstOrDefault(result => MatchesRequest(result, request));

        if (cachedResult is not null)
        {
            return OcrTextExtractionResult.Success(cachedResult, wasFromCache: true);
        }

        OcrRecognitionResult recognitionResult = await _ocrProvider
            .RecognizeAsync(
                new OcrRequest(
                    request.DocumentId,
                    request.SourceImage,
                    request.SourceRegion,
                    request.LanguageTag),
                cancellationToken)
            .ConfigureAwait(false);

        if (!recognitionResult.Succeeded || recognitionResult.Result is not OcrResult result)
        {
            return OcrTextExtractionResult.Failed(
                recognitionResult.Failure ?? new OcrFailure(
                    OcrFailureReason.Unknown,
                    "OCR did not return a result."));
        }

        await SaveResultAsync(cacheDocument, result, cancellationToken).ConfigureAwait(false);
        return OcrTextExtractionResult.Success(result, wasFromCache: false);
    }

    public async Task InvalidateAsync(
        DocumentId documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _resultCache
            .DeleteAsync(documentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SaveResultAsync(
        OcrCacheDocument cacheDocument,
        OcrResult result,
        CancellationToken cancellationToken)
    {
        List<OcrResult> results = cacheDocument
            .Results
            .Where(candidate => !MatchesCachedResult(candidate, result))
            .ToList();
        results.Add(result);

        await _resultCache
            .SaveAsync(
                new OcrCacheDocument(
                    result.DocumentId,
                    result.RecognizedAtUtc,
                    results),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool MatchesRequest(
        OcrResult result,
        OcrTextExtractionRequest request)
    {
        if (result.DocumentId != request.DocumentId)
        {
            return false;
        }

        if (!RegionsAreEquivalent(result.SourceRegion, request.SourceRegion))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(request.LanguageTag)
            || string.Equals(result.LanguageTag, request.LanguageTag, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCachedResult(
        OcrResult first,
        OcrResult second)
    {
        return first.DocumentId == second.DocumentId
            && RegionsAreEquivalent(first.SourceRegion, second.SourceRegion)
            && string.Equals(first.LanguageTag, second.LanguageTag, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegionsAreEquivalent(
        RectD? first,
        RectD? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        RectD firstValue = first.Value;
        RectD secondValue = second.Value;

        return Math.Abs(firstValue.X - secondValue.X) < RegionTolerance
            && Math.Abs(firstValue.Y - secondValue.Y) < RegionTolerance
            && Math.Abs(firstValue.Width - secondValue.Width) < RegionTolerance
            && Math.Abs(firstValue.Height - secondValue.Height) < RegionTolerance;
    }
}
