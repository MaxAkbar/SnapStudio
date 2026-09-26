using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class FileSystemDocumentRepositoryTests
{
    [TestMethod]
    public async Task CreateSaveGetAsync_RoundTripsEditableDocument()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
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

        Assert.IsNotNull(loaded);
        Assert.AreEqual(document.Id, loaded.Id);
        Assert.AreEqual("source.png", loaded.SourceImage.Path);
        Assert.ContainsSingle(loaded.Annotations);
        Assert.AreEqual(AnnotationKind.Rectangle, loaded.Annotations[0].Kind);
        Assert.AreEqual("test", loaded.Metadata.Properties["source"]);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesPersistedDocument()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
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

        Assert.IsTrue(deleted);
        Assert.IsNull(loaded);
        Assert.IsFalse(deletedAgain);
        Assert.IsTrue(File.Exists(sourcePath));
    }

    [TestMethod]
    public async Task SaveAsync_OverlappingColorChanges_PersistsLastRequestedSnapshot()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var rectangle = new AnnotationObject { Kind = AnnotationKind.Rectangle };
        var document = new CaptureDocument { Annotations = [rectangle] };
        await repository.SaveAsync(document, CancellationToken.None);

        // Keep the first write busy while the subsequent picker events request saves.
        document.Metadata.Properties["payload"] = new string('x', 4 * 1024 * 1024);
        var saves = new List<Task> { repository.SaveAsync(document, CancellationToken.None) };
        document.Metadata.Properties.Remove("payload");
        for (byte red = 1; red <= 64; red++)
        {
            rectangle.Style = rectangle.Style with { Stroke = new ColorRgba(red, 20, 30, 255) };
            saves.Add(repository.SaveAsync(document, CancellationToken.None));
        }

        ColorRgba lastSavedColor = rectangle.Style.Stroke;
        DateTimeOffset lastSavedAt = document.Metadata.ModifiedAtUtc;
        rectangle.Style = rectangle.Style with { Stroke = ColorRgba.White };
        await Task.WhenAll(saves);

        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(lastSavedColor, Assert.ContainsSingle(loaded.Annotations).Style.Stroke);
        Assert.AreEqual(lastSavedAt, loaded.Metadata.ModifiedAtUtc);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task SaveAsync_WhenCanceled_DoesNotChangeMetadataOrPersistEdits()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var document = new CaptureDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        DocumentMetadata savedMetadata = document.Metadata;
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.SaveAsync(document, cancellation.Token));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(savedMetadata, document.Metadata);
        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.IsEmpty(loaded.Annotations);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path, "*.tmp", SearchOption.AllDirectories));

        await repository.SaveAsync(document, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.ContainsSingle(loaded.Annotations);
    }

    [TestMethod]
    public async Task SaveAsync_WhenReplacementFails_PreservesDocumentAndAllowsNextSave()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var document = new CaptureDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle });
        string documentPath = Path.Combine(workspace.Path, document.Id.ToString(), "document.snapstudio.json");

        // An external reader that does not share deletion must still report a real I/O failure.
        using (FileStream lockedDocument = File.OpenRead(documentPath))
        {
            await Assert.ThrowsExactlyAsync<IOException>(
                () => repository.SaveAsync(document, CancellationToken.None));
        }

        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.IsEmpty(loaded.Annotations);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path, "*.tmp", SearchOption.AllDirectories));

        await repository.SaveAsync(document, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.ContainsSingle(loaded.Annotations);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SaveAsync_CanceledDuringWriteOrWhileQueued_CleansUpAndAllowsNextSave(bool cancelQueuedSave)
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var document = new CaptureDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        document.Metadata.Properties["payload"] = new string('x', 8 * 1024 * 1024);
        Task firstSave = repository.SaveAsync(
            document,
            cancelQueuedSave ? CancellationToken.None : cancellation.Token);
        document.Metadata.Properties.Remove("payload");
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle });
        Task canceledSave = cancelQueuedSave
            ? repository.SaveAsync(document, cancellation.Token)
            : firstSave;
        cancellation.Cancel();

        Task assertCancellation = Assert.ThrowsAsync<OperationCanceledException>(() => canceledSave);
        await Task.WhenAll(cancelQueuedSave ? firstSave : Task.CompletedTask, assertCancellation);

        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.IsEmpty(loaded.Annotations);
        Assert.AreEqual(cancelQueuedSave, loaded.Metadata.Properties.ContainsKey("payload"));
        Assert.IsEmpty(Directory.GetFiles(workspace.Path, "*.tmp", SearchOption.AllDirectories));

        await repository.SaveAsync(document, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.ContainsSingle(loaded.Annotations);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SaveAsync_WhileReadingDocument_AllowsReplacement(bool readThroughCatalog)
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);
        var document = new CaptureDocument();
        document.Metadata.Properties["payload"] = new string('x', 4 * 1024 * 1024);
        await repository.SaveAsync(document, CancellationToken.None);

        Task read = readThroughCatalog
            ? ReadCatalogAsync()
            : ReadDocumentAsync();
        document.Metadata.Properties.Remove("payload");
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle });
        await Task.WhenAll(read, repository.SaveAsync(document, CancellationToken.None));

        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.ContainsSingle(loaded.Annotations);

        async Task ReadDocumentAsync()
        {
            CaptureDocument? snapshot = await repository.GetAsync(document.Id, CancellationToken.None);
            Assert.IsNotNull(snapshot);
            Assert.IsEmpty(snapshot.Annotations);
        }

        async Task ReadCatalogAsync()
        {
            DocumentSummary snapshot = Assert.ContainsSingle(await catalog.GetRecentAsync(10, CancellationToken.None));
            Assert.AreEqual(document.Id, snapshot.Id);
            Assert.AreEqual(0, snapshot.AnnotationCount);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
