using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsBoundsScrollingFrameCaptureServiceTests
{
    [Fact]
    public async Task CaptureFrameAsync_WhenBoundsAreValid_WritesFrameWithMetadata()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new FakeScrollingFrameImageWriter();
        var service = new WindowsBoundsScrollingFrameCaptureService(writer);
        ScrollTargetCandidate target = CreateTarget(new RectD(10, 20, 120, 80));

        ScrollingFrameCaptureResult result = await service.CaptureFrameAsync(
            new ScrollingFrameCaptureRequest(target, 2, workspace.Path),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Frame);
        Assert.Equal(2, result.Frame.Index);
        Assert.Equal(target.Bounds, writer.Bounds);
        Assert.StartsWith(workspace.Path, result.Frame.Image.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(120, result.Frame.Image.Width);
        Assert.Equal(80, result.Frame.Image.Height);
        Assert.Equal("gdi-copy-from-screen", result.Frame.Metadata["captureMethod"]);
        Assert.Equal(target.Id, result.Frame.Metadata["targetId"]);
        Assert.True(File.Exists(result.Frame.Image.Path));
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenBoundsAreInvalid_ReturnsInvalidRequest()
    {
        var service = new WindowsBoundsScrollingFrameCaptureService(new FakeScrollingFrameImageWriter());

        ScrollingFrameCaptureResult result = await service.CaptureFrameAsync(
            new ScrollingFrameCaptureRequest(
                CreateTarget(new RectD(0, 0, 1, 80)),
                0,
                Path.GetTempPath()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollingCaptureFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenWriterFails_ReturnsFrameCaptureFailure()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var service = new WindowsBoundsScrollingFrameCaptureService(new ThrowingScrollingFrameImageWriter());

        ScrollingFrameCaptureResult result = await service.CaptureFrameAsync(
            new ScrollingFrameCaptureRequest(
                CreateTarget(new RectD(0, 0, 100, 80)),
                0,
                workspace.Path),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollingCaptureFailureReason.FrameCaptureFailed, result.Failure?.Reason);
    }

    private static ScrollTargetCandidate CreateTarget(RectD bounds)
    {
        return new ScrollTargetCandidate(
            "uia:42.1",
            "Scrollable target",
            ScrollTargetKind.Control,
            bounds,
            new Dictionary<string, string>());
    }

    private sealed class FakeScrollingFrameImageWriter : IWindowsScrollingFrameImageWriter
    {
        public RectD Bounds { get; private set; }

        public ImageAsset Capture(
            RectD bounds,
            string outputPath,
            CancellationToken cancellationToken)
        {
            Bounds = bounds;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var bitmap = new Bitmap(
                (int)Math.Round(bounds.Width),
                (int)Math.Round(bounds.Height),
                PixelFormat.Format32bppPArgb);
            bitmap.Save(outputPath, ImageFormat.Png);

            return new ImageAsset(
                outputPath,
                bitmap.Width,
                bitmap.Height,
                ImagePixelFormat.Bgra32);
        }
    }

    private sealed class ThrowingScrollingFrameImageWriter : IWindowsScrollingFrameImageWriter
    {
        public ImageAsset Capture(
            RectD bounds,
            string outputPath,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Desktop capture failed.");
        }
    }
}
