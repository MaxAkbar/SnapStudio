using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class FileSystemDocumentThumbnailCacheTests
{
    [Fact]
    public async Task EnsureThumbnailAsync_GeneratesScaledThumbnailAndReusesCache()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        CaptureDocument document = await CreateDocumentAsync(repository, workspace.Path, clock);
        var cache = new FileSystemDocumentThumbnailCache(
            workspace.Path,
            repository,
            new SystemDrawingDocumentRenderer(repository));

        DocumentThumbnailResult generated = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 64),
            CancellationToken.None);
        DocumentThumbnailResult cached = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 64),
            CancellationToken.None);

        Assert.True(generated.Succeeded);
        Assert.True(generated.WasGenerated);
        Assert.True(File.Exists(generated.ThumbnailPath));
        using Bitmap thumbnail = new(generated.ThumbnailPath!);
        Assert.Equal(64, thumbnail.Width);
        Assert.Equal(32, thumbnail.Height);

        Assert.True(cached.Succeeded);
        Assert.False(cached.WasGenerated);
        Assert.Equal(generated.ThumbnailPath, cached.ThumbnailPath);
    }

    [Fact]
    public async Task EnsureThumbnailAsync_RegeneratesWhenDocumentChanges()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        CaptureDocument document = await CreateDocumentAsync(repository, workspace.Path, clock);
        var cache = new FileSystemDocumentThumbnailCache(
            workspace.Path,
            repository,
            new SystemDrawingDocumentRenderer(repository));

        DocumentThumbnailResult first = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 64),
            CancellationToken.None);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            Bounds = new RectD(10, 10, 40, 30)
        });
        await repository.SaveAsync(document, CancellationToken.None);

        DocumentThumbnailResult regenerated = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 64),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(regenerated.Succeeded);
        Assert.True(regenerated.WasGenerated);
        Assert.NotEqual(first.ThumbnailPath, regenerated.ThumbnailPath);
        Assert.False(File.Exists(first.ThumbnailPath));
        Assert.True(File.Exists(regenerated.ThumbnailPath));
    }

    [Fact]
    public async Task EnsureThumbnailAsync_RegeneratesAfterRapidAnnotationSave()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        CaptureDocument document = await CreateDocumentAsync(repository, workspace.Path, clock);
        var cache = new FileSystemDocumentThumbnailCache(
            workspace.Path,
            repository,
            new SystemDrawingDocumentRenderer(repository));

        document.Annotations.Add(CreateFilledRectangle(
            new RectD(10, 10, 30, 30),
            new ColorRgba(220, 0, 0, 255)));
        await repository.SaveAsync(document, CancellationToken.None);

        DocumentThumbnailResult first = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 200),
            CancellationToken.None);

        document.Annotations.Add(CreateFilledRectangle(
            new RectD(60, 10, 30, 30),
            new ColorRgba(0, 0, 220, 255)));
        await repository.SaveAsync(document, CancellationToken.None);

        DocumentThumbnailResult regenerated = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 200),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(regenerated.Succeeded);
        Assert.True(regenerated.WasGenerated);
        Assert.NotEqual(first.ThumbnailPath, regenerated.ThumbnailPath);
        using Bitmap thumbnail = new(regenerated.ThumbnailPath!);
        Color secondAnnotationPixel = thumbnail.GetPixel(70, 20);
        Assert.True(secondAnnotationPixel.B > 180);
        Assert.True(secondAnnotationPixel.R < 80);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsCachedThumbnailPath()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        var catalog = new FileSystemDocumentCatalog(workspace.Path);
        CaptureDocument document = await CreateDocumentAsync(repository, workspace.Path, clock);
        var cache = new FileSystemDocumentThumbnailCache(
            workspace.Path,
            repository,
            new SystemDrawingDocumentRenderer(repository));

        DocumentThumbnailResult generated = await cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 64),
            CancellationToken.None);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(
            10,
            CancellationToken.None);

        DocumentSummary summary = Assert.Single(summaries);
        Assert.Equal(generated.ThumbnailPath, summary.ThumbnailPath);
    }

    private static async Task<CaptureDocument> CreateDocumentAsync(
        FileSystemDocumentRepository repository,
        string workspacePath,
        MutableClock clock)
    {
        string sourcePath = Path.Combine(workspacePath, "source.png");
        CreateSolidImage(sourcePath, 200, 100, Color.White);
        var capture = new CaptureResult(
            CaptureId.New(),
            clock.UtcNow,
            new ImageAsset(sourcePath, 200, 100, ImagePixelFormat.Bgra32),
            new Dictionary<string, string> { ["title"] = "Thumbnail Test" });

        return await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
    }

    private static AnnotationObject CreateFilledRectangle(
        RectD bounds,
        ColorRgba fill)
    {
        return new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            Bounds = bounds,
            Style = new AnnotationStyle(
                ColorRgba.Transparent,
                fill,
                ColorRgba.Black,
                1,
                1)
        };
    }

    private static void CreateSolidImage(
        string path,
        int width,
        int height,
        Color color)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
