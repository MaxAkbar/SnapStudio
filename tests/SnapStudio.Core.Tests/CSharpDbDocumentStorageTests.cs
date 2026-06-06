using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class CSharpDbDocumentStorageTests
{
    [Fact]
    public async Task CreateSaveGetAsync_RoundTripsEditableDocument()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        var capture = new CaptureResult(
            CaptureId.New(),
            clock.UtcNow,
            new ImageAsset("source.png", 800, 600, ImagePixelFormat.Rgba32),
            new Dictionary<string, string> { ["source"] = "test" });

        CaptureDocument document = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            Bounds = new RectD(10, 20, 300, 200),
            Style = AnnotationStyle.Default
        });

        await repository.SaveAsync(document, CancellationToken.None);

        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(document.Id, loaded.Id);
        Assert.Equal("source.png", loaded.SourceImage.Path);
        Assert.Single(loaded.Annotations);
        Assert.Equal(AnnotationKind.Rectangle, loaded.Annotations[0].Kind);
        Assert.Equal("test", loaded.Metadata.Properties["source"]);
        Assert.True(File.Exists(store.DatabasePath));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsDocumentsOrderedByModifiedDate()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        var catalog = new CSharpDbDocumentCatalog(store);

        CaptureDocument older = await CreateDocumentAsync(repository, "Older", clock.UtcNow);
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        CaptureDocument newer = await CreateDocumentAsync(repository, "Newer", clock.UtcNow);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(10, CancellationToken.None);

        Assert.Equal([newer.Id, older.Id], summaries.Select(summary => summary.Id));
        Assert.Equal("Newer", summaries[0].Title);
        Assert.Equal("Older", summaries[1].Title);

        await repository.DeleteAsync(newer.Id, CancellationToken.None);

        summaries = await catalog.GetRecentAsync(10, CancellationToken.None);

        DocumentSummary remaining = Assert.Single(summaries);
        Assert.Equal(older.Id, remaining.Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocumentRowAndKeepsSourceImage()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        await File.WriteAllTextAsync(sourcePath, "image");
        var capture = new CaptureResult(
            CaptureId.New(),
            clock.UtcNow,
            new ImageAsset(sourcePath, 800, 600, ImagePixelFormat.Rgba32),
            new Dictionary<string, string>());

        CaptureDocument document = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);

        bool deleted = await repository.DeleteAsync(document.Id, CancellationToken.None);
        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        bool deletedAgain = await repository.DeleteAsync(document.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(loaded);
        Assert.False(deletedAgain);
        Assert.True(File.Exists(sourcePath));
    }

    private static async Task<CaptureDocument> CreateDocumentAsync(
        IDocumentRepository repository,
        string title,
        DateTimeOffset capturedAtUtc)
    {
        var capture = new CaptureResult(
            CaptureId.New(),
            capturedAtUtc,
            new ImageAsset($"{title}.png", 800, 600, ImagePixelFormat.Rgba32),
            new Dictionary<string, string>
            {
                ["title"] = title,
                ["source"] = "capture"
            });

        return await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
