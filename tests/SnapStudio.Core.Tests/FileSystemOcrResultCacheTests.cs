using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class FileSystemOcrResultCacheTests
{
    [Fact]
    public async Task GetAsync_WhenCacheIsMissing_ReturnsEmptyDocumentCache()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var documentId = DocumentId.New();
        var cache = new FileSystemOcrResultCache(workspace.Path);

        OcrCacheDocument cacheDocument = await cache.GetAsync(
            documentId,
            CancellationToken.None);

        Assert.Equal(documentId, cacheDocument.DocumentId);
        Assert.Empty(cacheDocument.Results);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsMultipleSelectionResults()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var documentId = DocumentId.New();
        DateTimeOffset recognizedAtUtc = new(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);
        var cache = new FileSystemOcrResultCache(workspace.Path);
        var original = new OcrCacheDocument(
            documentId,
            recognizedAtUtc,
            [
                CreateResult(
                    documentId,
                    recognizedAtUtc,
                    new RectD(10, 20, 300, 80),
                    "SnapStudio OCR"),
                CreateResult(
                    documentId,
                    recognizedAtUtc.AddSeconds(1),
                    new RectD(10, 120, 200, 60),
                    "Second region")
            ]);

        await cache.SaveAsync(original, CancellationToken.None);

        OcrCacheDocument loaded = await cache.GetAsync(
            documentId,
            CancellationToken.None);

        Assert.Equal(documentId, loaded.DocumentId);
        Assert.Equal(recognizedAtUtc, loaded.UpdatedAtUtc);
        Assert.Equal(2, loaded.Results.Count);
        Assert.Equal("SnapStudio OCR", loaded.Results[0].Text);
        Assert.Equal("Second region", loaded.Results[1].Text);
        Assert.Equal(new RectD(10, 20, 300, 80), loaded.Results[0].SourceRegion);
    }

    [Fact]
    public async Task DeleteAsync_RemovesExistingCacheFile()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var documentId = DocumentId.New();
        DateTimeOffset recognizedAtUtc = new(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);
        var cache = new FileSystemOcrResultCache(workspace.Path);
        var original = new OcrCacheDocument(
            documentId,
            recognizedAtUtc,
            [CreateResult(documentId, recognizedAtUtc, null, "Text")]);

        await cache.SaveAsync(original, CancellationToken.None);

        bool deleted = await cache.DeleteAsync(documentId, CancellationToken.None);
        bool deletedAgain = await cache.DeleteAsync(documentId, CancellationToken.None);
        OcrCacheDocument loaded = await cache.GetAsync(documentId, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(deletedAgain);
        Assert.Empty(loaded.Results);
    }

    [Fact]
    public async Task GetAsync_WhenCacheJsonIsInvalid_ReturnsEmptyDocumentCache()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var documentId = DocumentId.New();
        string documentDirectory = Path.Combine(workspace.Path, documentId.ToString());
        Directory.CreateDirectory(documentDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(documentDirectory, "ocr-cache.snapstudio.json"),
            "{ invalid json",
            CancellationToken.None);
        var cache = new FileSystemOcrResultCache(workspace.Path);

        OcrCacheDocument loaded = await cache.GetAsync(
            documentId,
            CancellationToken.None);

        Assert.Equal(documentId, loaded.DocumentId);
        Assert.Empty(loaded.Results);
    }

    private static OcrResult CreateResult(
        DocumentId documentId,
        DateTimeOffset recognizedAtUtc,
        RectD? sourceRegion,
        string text)
    {
        return new OcrResult(
            Guid.NewGuid(),
            documentId,
            recognizedAtUtc,
            sourceRegion,
            "en-US",
            [
                new OcrTextLine(
                    text,
                    new RectD(1, 2, 50, 10),
                    [
                        new OcrTextWord(
                            text,
                            new RectD(1, 2, 50, 10),
                            0.98)
                    ])
            ]);
    }
}
