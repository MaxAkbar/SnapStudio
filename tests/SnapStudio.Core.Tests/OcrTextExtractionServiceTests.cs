using SnapStudio.Core.Capture;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class OcrTextExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_WhenMatchingResultIsCached_ReturnsCacheWithoutCallingProvider()
    {
        var documentId = DocumentId.New();
        var region = new RectD(10, 20, 100, 40);
        OcrResult cachedResult = CreateResult(documentId, region, "Cached text");
        var provider = new FakeOcrProvider(OcrRecognitionResult.Failed(
            new OcrFailure(OcrFailureReason.Unknown, "Provider should not be called.")));
        var cache = new FakeOcrResultCache(new OcrCacheDocument(
            documentId,
            cachedResult.RecognizedAtUtc,
            [cachedResult]));
        var service = new OcrTextExtractionService(provider, cache);

        OcrTextExtractionResult result = await service.ExtractAsync(
            CreateRequest(documentId, region),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.WasFromCache);
        Assert.Equal("Cached text", result.Result?.Text);
        Assert.Equal(0, provider.RecognitionCallCount);
    }

    [Fact]
    public async Task ExtractAsync_WhenCacheMisses_SavesProviderResult()
    {
        var documentId = DocumentId.New();
        var region = new RectD(10, 20, 100, 40);
        OcrResult providerResult = CreateResult(documentId, region, "Recognized text");
        var provider = new FakeOcrProvider(OcrRecognitionResult.Success(providerResult));
        var cache = new FakeOcrResultCache(OcrCacheDocument.Empty(documentId));
        var service = new OcrTextExtractionService(provider, cache);

        OcrTextExtractionResult result = await service.ExtractAsync(
            CreateRequest(documentId, region),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.WasFromCache);
        Assert.Equal("Recognized text", result.Result?.Text);
        Assert.Equal(1, provider.RecognitionCallCount);
        Assert.NotNull(cache.SavedDocument);
        Assert.Single(cache.SavedDocument.Results);
    }

    [Fact]
    public async Task ExtractAsync_WhenProviderFails_ReturnsFailureWithoutSaving()
    {
        var documentId = DocumentId.New();
        var provider = new FakeOcrProvider(OcrRecognitionResult.Failed(
            new OcrFailure(OcrFailureReason.ProviderUnavailable, "OCR unavailable.")));
        var cache = new FakeOcrResultCache(OcrCacheDocument.Empty(documentId));
        var service = new OcrTextExtractionService(provider, cache);

        OcrTextExtractionResult result = await service.ExtractAsync(
            CreateRequest(documentId, null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OcrFailureReason.ProviderUnavailable, result.Failure?.Reason);
        Assert.Null(cache.SavedDocument);
    }

    [Fact]
    public async Task InvalidateAsync_DeletesCachedDocumentResults()
    {
        var documentId = DocumentId.New();
        var provider = new FakeOcrProvider(OcrRecognitionResult.Failed(
            new OcrFailure(OcrFailureReason.Unknown, "Not used.")));
        var cache = new FakeOcrResultCache(OcrCacheDocument.Empty(documentId));
        var service = new OcrTextExtractionService(provider, cache);

        await service.InvalidateAsync(documentId, CancellationToken.None);

        Assert.Equal(documentId, cache.DeletedDocumentId);
    }

    private static OcrTextExtractionRequest CreateRequest(
        DocumentId documentId,
        RectD? region)
    {
        return new OcrTextExtractionRequest(
            documentId,
            new ImageAsset("source.png", 320, 200, ImagePixelFormat.Bgra32),
            region);
    }

    private static OcrResult CreateResult(
        DocumentId documentId,
        RectD? region,
        string text)
    {
        return new OcrResult(
            Guid.NewGuid(),
            documentId,
            new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero),
            region,
            "en-US",
            [
                new OcrTextLine(
                    text,
                    new RectD(1, 2, 20, 10),
                    [
                        new OcrTextWord(
                            text,
                            new RectD(1, 2, 20, 10),
                            null)
                    ])
            ]);
    }

    private sealed class FakeOcrProvider(OcrRecognitionResult result) : IOcrProvider
    {
        public int RecognitionCallCount { get; private set; }

        public Task<OcrProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(OcrProviderStatus.Available("Fake OCR", ["en-US"]));
        }

        public Task<OcrRecognitionResult> RecognizeAsync(
            OcrRequest request,
            CancellationToken cancellationToken)
        {
            RecognitionCallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeOcrResultCache(OcrCacheDocument cacheDocument) : IOcrResultCache
    {
        public OcrCacheDocument? SavedDocument { get; private set; }

        public DocumentId? DeletedDocumentId { get; private set; }

        public Task<OcrCacheDocument> GetAsync(
            DocumentId documentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(cacheDocument);
        }

        public Task SaveAsync(
            OcrCacheDocument cacheDocument,
            CancellationToken cancellationToken)
        {
            SavedDocument = cacheDocument;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            DocumentId documentId,
            CancellationToken cancellationToken)
        {
            DeletedDocumentId = documentId;
            return Task.FromResult(true);
        }
    }
}
