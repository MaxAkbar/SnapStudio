using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Core.System;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class FileSystemDocumentThumbnailCacheTests
{
    [Fact]
    public async Task EnsureThumbnailAsync_SupersededRefreshFinishes128PixelRenderWithoutCancellation()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        using var lifetime = new CancellationTokenSource();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 749, 440, Color.White);
        CaptureDocument document = await repository.CreateFromCaptureAsync(
            new CaptureResult(
                CaptureId.New(), clock.UtcNow,
                new ImageAsset(sourcePath, 749, 440, ImagePixelFormat.Bgra32),
                new Dictionary<string, string>()),
            lifetime.Token);
        document.Annotations.Add(CreateFilledRectangle(new RectD(40, 40, 200, 160), ColorRgba.Black));
        await repository.SaveAsync(document, lifetime.Token);
        var renderer = new PausingRenderer(new SystemDrawingDocumentRenderer(repository));
        var cache = new FileSystemDocumentThumbnailCache(workspace.Path, repository, renderer);
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        var summary = new DocumentSummary(
            document.Id, "Reported thumbnail", document.Metadata.CreatedAtUtc,
            document.Metadata.ModifiedAtUtc, sourcePath, 1);
        var publishedPaths = new List<string>();

        async Task<string?> ResolveAsync(DocumentSummary item, CancellationToken token)
        {
            DocumentThumbnailResult result = await cache.EnsureThumbnailAsync(
                new DocumentThumbnailRequest(item.Id, item.ModifiedAtUtc, 128), token);
            Assert.True(result.Succeeded, result.ErrorMessage);
            return result.ThumbnailPath;
        }

        Task oldRefresh = coordinator.RefreshAsync(
            [summary], ResolveAsync, (_, path) => publishedPaths.Add(path), lifetime.Token);
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task latestRefresh = coordinator.RefreshAsync(
            [summary], ResolveAsync, (_, path) => publishedPaths.Add(path), lifetime.Token);

        Assert.Equal(lifetime.Token, renderer.Token);
        Assert.False(renderer.Token.IsCancellationRequested);
        Assert.NotNull(renderer.Request);
        Assert.Equal(128d / 749, renderer.Request.Scale);
        Assert.Null(renderer.Request.Viewport);
        renderer.Continue.SetResult();
        await Task.WhenAll(oldRefresh, latestRefresh).WaitAsync(TimeSpan.FromSeconds(5));

        string thumbnailPath = Assert.Single(publishedPaths);
        using Bitmap thumbnail = new(thumbnailPath);
        Assert.Equal(128, thumbnail.Width);
        Assert.Equal(75, thumbnail.Height);
        Assert.Equal(1, renderer.RenderCount);
        Assert.True(oldRefresh.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task EnsureThumbnailAsync_RenderCancellationPropagatesWithoutCaching()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        using var lifetime = new CancellationTokenSource();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var repository = new FileSystemDocumentRepository(workspace.Path, clock);
        CaptureDocument document = await CreateDocumentAsync(repository, workspace.Path, clock);
        var renderer = new PausingRenderer(new SystemDrawingDocumentRenderer(repository));
        var cache = new FileSystemDocumentThumbnailCache(workspace.Path, repository, renderer);

        Task<DocumentThumbnailResult> pending = cache.EnsureThumbnailAsync(
            new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 128),
            lifetime.Token);
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(lifetime.Token, exception.CancellationToken);
        Assert.Empty(Directory.EnumerateFiles(workspace.Path, "thumbnail-*.png", SearchOption.AllDirectories));
    }

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

    private sealed class PausingRenderer(IDocumentRenderer inner) : IDocumentRenderer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RenderRequest? Request { get; private set; }

        public CancellationToken Token { get; private set; }

        public int RenderCount { get; private set; }

        public async Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            RenderCount++;
            Request = request;
            Token = cancellationToken;
            Started.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            return await inner.RenderAsync(request, cancellationToken);
        }
    }
}
