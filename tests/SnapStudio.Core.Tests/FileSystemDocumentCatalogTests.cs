using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class FileSystemDocumentCatalogTests
{
    [Fact]
    public async Task GetRecentAsync_ReturnsDocumentsOrderedByModifiedDate()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);

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
    public async Task EnsureInitializedAsync_CreatesSeedDocumentOnlyWhenCatalogIsEmpty()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);
        var bootstrapper = new DocumentWorkspaceBootstrapper(workspace.Path, repository, catalog);

        await bootstrapper.EnsureInitializedAsync(CancellationToken.None);
        await bootstrapper.EnsureInitializedAsync(CancellationToken.None);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(10, CancellationToken.None);

        DocumentSummary summary = Assert.Single(summaries);
        Assert.Equal("Welcome Capture", summary.Title);
        Assert.Equal("seed://welcome", summary.SourceImagePath);
        Assert.Equal("seed", summary.SourceKind);
    }

    [Fact]
    public async Task GetRecentAsync_UsesNewestExistingThumbnailWhenExactThumbnailIsMissing()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);
        CaptureDocument document = await CreateDocumentAsync(repository, "Thumbnail", clock.UtcNow);
        string documentDirectory = Path.Combine(workspace.Path, document.Id.ToString());
        string olderThumbnailPath = Path.Combine(documentDirectory, "thumbnail-1.png");
        string newerThumbnailPath = Path.Combine(documentDirectory, "thumbnail-2.png");
        await File.WriteAllTextAsync(olderThumbnailPath, "older", CancellationToken.None);
        await File.WriteAllTextAsync(newerThumbnailPath, "newer", CancellationToken.None);
        File.SetLastWriteTimeUtc(olderThumbnailPath, clock.UtcNow.UtcDateTime);
        File.SetLastWriteTimeUtc(newerThumbnailPath, clock.UtcNow.AddMinutes(1).UtcDateTime);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(
            10,
            CancellationToken.None);

        DocumentSummary summary = Assert.Single(summaries);
        Assert.Equal(newerThumbnailPath, summary.ThumbnailPath);
    }

    [Fact]
    public async Task GetRecentAsync_ReportsSourceKindFromDocumentMetadata()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);

        await CreateDocumentAsync(
            repository,
            "Imported",
            DateTimeOffset.UtcNow,
            "import");

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(
            10,
            CancellationToken.None);

        DocumentSummary summary = Assert.Single(summaries);
        Assert.Equal("import", summary.SourceKind);
    }

    [Fact]
    public async Task GetRecentAsync_WhenSourceMetadataIsMissing_UsesCaptureSourceKind()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);

        await CreateDocumentAsync(
            repository,
            "Capture",
            DateTimeOffset.UtcNow,
            sourceKind: null);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(
            10,
            CancellationToken.None);

        DocumentSummary summary = Assert.Single(summaries);
        Assert.Equal("capture", summary.SourceKind);
    }

    [Fact]
    public async Task GetRecentAsync_WhenDocumentIsDuplicate_ReportsDuplicateSourceKind()
    {
        using var workspace = TemporaryWorkspace.Create();
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);
        CaptureDocument original = await CreateDocumentAsync(
            repository,
            "Original",
            DateTimeOffset.UtcNow,
            "import");
        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(
            original,
            "Original Copy");
        await repository.SaveAsync(duplicate, CancellationToken.None);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(
            10,
            CancellationToken.None);

        DocumentSummary duplicateSummary = Assert.Single(
            summaries,
            summary => summary.Id == duplicate.Id);
        Assert.Equal("duplicate", duplicateSummary.SourceKind);
    }

    private static async Task<CaptureDocument> CreateDocumentAsync(
        FileSystemDocumentRepository repository,
        string title,
        DateTimeOffset capturedAtUtc,
        string? sourceKind = "capture")
    {
        var metadata = new Dictionary<string, string> { ["title"] = title };
        if (!string.IsNullOrWhiteSpace(sourceKind))
        {
            metadata["source"] = sourceKind;
        }

        var capture = new CaptureResult(
            CaptureId.New(),
            capturedAtUtc,
            new ImageAsset($"{title}.png", 800, 600, ImagePixelFormat.Rgba32),
            metadata);

        return await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
