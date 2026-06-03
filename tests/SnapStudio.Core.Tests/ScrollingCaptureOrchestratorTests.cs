using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Core.Tests;

public sealed class ScrollingCaptureOrchestratorTests
{
    [Fact]
    public async Task CaptureAsync_WhenScrollReachesEnd_StitchesCapturedFrames()
    {
        ScrollTargetCandidate target = CreateTarget();
        var frameCapture = new FakeFrameCaptureService(
            [
                ScrollingFrameCaptureResult.Success(CreateFrame(0)),
                ScrollingFrameCaptureResult.Success(CreateFrame(1))
            ]);
        var scrollInput = new FakeScrollInputController(
            [ScrollInputResult.Success(reachedEnd: true, 100, null)]);
        ImageAsset stitchedImage = CreateImage("stitched.png");
        var stitcher = new FakeStitcher(ScrollingStitchResult.Success(stitchedImage));
        var orchestrator = new ScrollingCaptureOrchestrator(frameCapture, scrollInput, stitcher);

        ScrollingCaptureResult result = await orchestrator.CaptureAsync(
            new ScrollingCaptureRequest(target, Path.GetTempPath(), MaximumFrames: 5),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.IsPartial);
        Assert.Equal(stitchedImage, result.Image);
        Assert.Equal(2, result.Frames.Count);
        Assert.Equal(1, scrollInput.CallCount);
        Assert.Equal(1, stitcher.CallCount);
    }

    [Fact]
    public async Task CaptureAsync_WhenDiagnosticsWriterSucceeds_AttachesBundlePath()
    {
        ScrollTargetCandidate target = CreateTarget();
        var frameCapture = new FakeFrameCaptureService(
            [ScrollingFrameCaptureResult.Success(CreateFrame(0))]);
        var scrollInput = new FakeScrollInputController(
            [ScrollInputResult.Success(reachedEnd: true, 100, null)]);
        var stitcher = new FakeStitcher(ScrollingStitchResult.Success(CreateImage("stitched.png")));
        var diagnosticsWriter = new FakeDiagnosticsBundleWriter(
            ScrollingCaptureDiagnosticsBundleResult.Success("diagnostics.json"));
        var orchestrator = new ScrollingCaptureOrchestrator(
            frameCapture,
            scrollInput,
            stitcher,
            diagnosticsWriter);

        ScrollingCaptureResult result = await orchestrator.CaptureAsync(
            new ScrollingCaptureRequest(target, Path.GetTempPath(), MaximumFrames: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("diagnostics.json", result.Diagnostics["diagnosticsBundlePath"]);
        Assert.NotNull(diagnosticsWriter.Request);
        Assert.Equal(target, diagnosticsWriter.Request.CaptureRequest.Target);
        Assert.True(diagnosticsWriter.Request.CaptureResult.Succeeded);
    }

    [Fact]
    public async Task CaptureAsync_WhenDiagnosticsWriterFails_PreservesCaptureResult()
    {
        var frameCapture = new FakeFrameCaptureService(
            [ScrollingFrameCaptureResult.Success(CreateFrame(0))]);
        var scrollInput = new FakeScrollInputController(
            [ScrollInputResult.Success(reachedEnd: true, 100, null)]);
        var stitcher = new FakeStitcher(ScrollingStitchResult.Success(CreateImage("stitched.png")));
        var diagnosticsWriter = new FakeDiagnosticsBundleWriter(
            ScrollingCaptureDiagnosticsBundleResult.Failed("Cannot write bundle."));
        var orchestrator = new ScrollingCaptureOrchestrator(
            frameCapture,
            scrollInput,
            stitcher,
            diagnosticsWriter);

        ScrollingCaptureResult result = await orchestrator.CaptureAsync(
            new ScrollingCaptureRequest(CreateTarget(), Path.GetTempPath(), MaximumFrames: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Cannot write bundle.", result.Diagnostics["diagnosticsBundleError"]);
    }

    [Fact]
    public async Task CaptureAsync_WhenScrollFailsAfterFrame_StitchesPartialOutput()
    {
        ScrollTargetCandidate target = CreateTarget();
        var frameCapture = new FakeFrameCaptureService(
            [ScrollingFrameCaptureResult.Success(CreateFrame(0))]);
        var scrollInput = new FakeScrollInputController(
            [
                ScrollInputResult.Failed(new ScrollInputFailure(
                    ScrollInputFailureReason.CannotScroll,
                    "Target stopped scrolling."))
            ]);
        ImageAsset partialImage = CreateImage("partial.png");
        var stitcher = new FakeStitcher(ScrollingStitchResult.Success(partialImage));
        var orchestrator = new ScrollingCaptureOrchestrator(frameCapture, scrollInput, stitcher);

        ScrollingCaptureResult result = await orchestrator.CaptureAsync(
            new ScrollingCaptureRequest(target, Path.GetTempPath(), MaximumFrames: 5),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.HasOutput);
        Assert.True(result.IsPartial);
        Assert.Equal(partialImage, result.Image);
        Assert.Equal(ScrollingCaptureFailureReason.ScrollFailed, result.Failure?.Reason);
        Assert.Single(result.Frames);
        Assert.Equal("ScrollFailed", result.Diagnostics["failureReason"]);
    }

    [Fact]
    public async Task CaptureAsync_WhenFirstFrameFails_ReturnsFailureWithoutStitching()
    {
        ScrollTargetCandidate target = CreateTarget();
        var failure = new ScrollingCaptureFailure(
            ScrollingCaptureFailureReason.FrameCaptureFailed,
            "No frame.");
        var frameCapture = new FakeFrameCaptureService(
            [ScrollingFrameCaptureResult.Failed(failure)]);
        var scrollInput = new FakeScrollInputController([]);
        var stitcher = new FakeStitcher(ScrollingStitchResult.Failed(
            new ScrollingCaptureFailure(ScrollingCaptureFailureReason.StitchFailed, "Not used.")));
        var orchestrator = new ScrollingCaptureOrchestrator(frameCapture, scrollInput, stitcher);

        ScrollingCaptureResult result = await orchestrator.CaptureAsync(
            new ScrollingCaptureRequest(target, Path.GetTempPath()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.HasOutput);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(0, stitcher.CallCount);
    }

    [Fact]
    public async Task CaptureAsync_WhenRequestIsInvalid_ReturnsInvalidRequest()
    {
        var orchestrator = new ScrollingCaptureOrchestrator(
            new FakeFrameCaptureService([]),
            new FakeScrollInputController([]),
            new FakeStitcher(ScrollingStitchResult.Failed(
                new ScrollingCaptureFailure(ScrollingCaptureFailureReason.StitchFailed, "Not used."))));

        ScrollingCaptureResult result = await orchestrator.CaptureAsync(
            new ScrollingCaptureRequest(CreateTarget(), Path.GetTempPath(), MaximumFrames: 0),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollingCaptureFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    private static ScrollTargetCandidate CreateTarget()
    {
        return new ScrollTargetCandidate(
            "uia:42.1",
            "Scrollable target",
            ScrollTargetKind.Control,
            new RectD(0, 0, 800, 600),
            new Dictionary<string, string> { ["runtimeId"] = "42.1" });
    }

    private static ScrollingCaptureFrame CreateFrame(int index)
    {
        return new ScrollingCaptureFrame(
            index,
            CreateImage($"frame-{index}.png"),
            new RectD(0, index * 500, 800, 600),
            index * 500,
            new Dictionary<string, string>());
    }

    private static ImageAsset CreateImage(string path)
    {
        return new ImageAsset(path, 800, 600, ImagePixelFormat.Bgra32);
    }

    private sealed class FakeFrameCaptureService(
        IReadOnlyList<ScrollingFrameCaptureResult> results) : IScrollingFrameCaptureService
    {
        private int _index;

        public Task<ScrollingFrameCaptureResult> CaptureFrameAsync(
            ScrollingFrameCaptureRequest request,
            CancellationToken cancellationToken)
        {
            ScrollingFrameCaptureResult result = _index < results.Count
                ? results[_index]
                : ScrollingFrameCaptureResult.Failed(new ScrollingCaptureFailure(
                    ScrollingCaptureFailureReason.FrameCaptureFailed,
                    "No fake frame was configured."));
            _index++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeScrollInputController(
        IReadOnlyList<ScrollInputResult> results) : IScrollInputController
    {
        private int _index;

        public int CallCount { get; private set; }

        public Task<ScrollInputResult> ScrollAsync(
            ScrollInputRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ScrollInputResult result = _index < results.Count
                ? results[_index]
                : ScrollInputResult.Success(reachedEnd: true, 100, null);
            _index++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeStitcher(ScrollingStitchResult result) : IScrollingStitcher
    {
        public int CallCount { get; private set; }

        public Task<ScrollingStitchResult> StitchAsync(
            ScrollingStitchRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeDiagnosticsBundleWriter(
        ScrollingCaptureDiagnosticsBundleResult result) : IScrollingCaptureDiagnosticsBundleWriter
    {
        public ScrollingCaptureDiagnosticsBundleRequest? Request { get; private set; }

        public Task<ScrollingCaptureDiagnosticsBundleResult> WriteAsync(
            ScrollingCaptureDiagnosticsBundleRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }
}
