using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class FileSystemDocumentRepositoryTests
{
    [Fact]
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

        Assert.NotNull(loaded);
        Assert.Equal(document.Id, loaded.Id);
        Assert.Equal("source.png", loaded.SourceImage.Path);
        Assert.Single(loaded.Annotations);
        Assert.Equal(AnnotationKind.Rectangle, loaded.Annotations[0].Kind);
        Assert.Equal("test", loaded.Metadata.Properties["source"]);
    }

    [Fact]
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

        Assert.True(deleted);
        Assert.Null(loaded);
        Assert.False(deletedAgain);
        Assert.True(File.Exists(sourcePath));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
