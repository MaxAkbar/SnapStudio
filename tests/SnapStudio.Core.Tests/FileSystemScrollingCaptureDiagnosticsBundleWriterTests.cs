using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class FileSystemScrollingCaptureDiagnosticsBundleWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesRedactedBundleJson()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new FileSystemScrollingCaptureDiagnosticsBundleWriter();
        ScrollingCaptureRequest captureRequest = new(
            new ScrollTargetCandidate(
                "uia:42.1",
                "Document user@example.com https://example.com/private",
                ScrollTargetKind.Browser,
                new RectD(10, 20, 800, 600),
                new Dictionary<string, string>
                {
                    ["sourcePath"] = @"C:\Users\maxim\Pictures\source.png",
                    ["sourceUrl"] = "https://example.com/private/source"
                }),
            workspace.Path,
            MaximumFrames: 5);
        ScrollingCaptureFrame frame = CreateFrame(
            0,
            @"C:\Users\maxim\AppData\Local\SnapStudio\frame-0.png");
        ScrollingCaptureResult captureResult = ScrollingCaptureResult.Partial(
            new ImageAsset(
                @"C:\Users\maxim\AppData\Local\SnapStudio\stitched.png",
                800,
                900,
                ImagePixelFormat.Bgra32),
            [frame],
            new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.ScrollFailed,
                @"Could not scroll C:\Users\maxim\Pictures\source.png for user@example.com at https://example.com/private/source."),
            new Dictionary<string, string>
            {
                ["sourceImagePath"] = @"C:\Users\maxim\AppData\Local\SnapStudio\frame-0.png",
                ["sourceUrl"] = "https://example.com/private/source"
            });

        ScrollingCaptureDiagnosticsBundleResult result = await writer.WriteAsync(
            new ScrollingCaptureDiagnosticsBundleRequest(captureRequest, captureResult),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.BundlePath);
        Assert.True(File.Exists(result.BundlePath));

        string contents = await File.ReadAllTextAsync(result.BundlePath);
        Assert.Contains("\"schemaVersion\": 1", contents);
        Assert.Contains("\"status\": \"partial\"", contents);
        Assert.Contains("\"frameCount\": 1", contents);
        Assert.Contains("<path>", contents);
        Assert.Contains("<email>", contents);
        Assert.Contains("<url>", contents);
        Assert.DoesNotContain(@"C:\Users\maxim", contents);
        Assert.DoesNotContain("user@example.com", contents);
        Assert.DoesNotContain("https://example.com/private", contents);
    }

    [Fact]
    public async Task WriteAsync_WhenOutputDirectoryIsBlank_ReturnsFailure()
    {
        var writer = new FileSystemScrollingCaptureDiagnosticsBundleWriter();

        ScrollingCaptureDiagnosticsBundleResult result = await writer.WriteAsync(
            new ScrollingCaptureDiagnosticsBundleRequest(
                new ScrollingCaptureRequest(CreateTarget(), string.Empty),
                ScrollingCaptureResult.Failed(new ScrollingCaptureFailure(
                    ScrollingCaptureFailureReason.InvalidRequest,
                    "Invalid request."))),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("output directory", result.ErrorMessage);
    }

    private static ScrollTargetCandidate CreateTarget()
    {
        return new ScrollTargetCandidate(
            "uia:42.1",
            "Scrollable target",
            ScrollTargetKind.Control,
            new RectD(0, 0, 800, 600),
            new Dictionary<string, string>());
    }

    private static ScrollingCaptureFrame CreateFrame(int index, string imagePath)
    {
        return new ScrollingCaptureFrame(
            index,
            new ImageAsset(imagePath, 800, 600, ImagePixelFormat.Bgra32),
            new RectD(0, 0, 800, 600),
            index,
            new Dictionary<string, string>
            {
                ["capturedPath"] = imagePath
            });
    }
}
