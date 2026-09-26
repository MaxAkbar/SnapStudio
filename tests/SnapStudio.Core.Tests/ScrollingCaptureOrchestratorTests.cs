using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ScrollingCaptureOrchestratorTests
{
    [TestMethod]
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

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.IsPartial);
        Assert.AreEqual(stitchedImage, result.Image);
        Assert.AreEqual(2, result.Frames.Count);
        Assert.AreEqual(1, scrollInput.CallCount);
        Assert.AreEqual(1, stitcher.CallCount);
    }

    [TestMethod]
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

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("diagnostics.json", result.Diagnostics["diagnosticsBundlePath"]);
        Assert.IsNotNull(diagnosticsWriter.Request);
        Assert.AreEqual(target, diagnosticsWriter.Request.CaptureRequest.Target);
        Assert.IsTrue(diagnosticsWriter.Request.CaptureResult.Succeeded);
    }

    [TestMethod]
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

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Cannot write bundle.", result.Diagnostics["diagnosticsBundleError"]);
    }

    [TestMethod]
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

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.HasOutput);
        Assert.IsTrue(result.IsPartial);
        Assert.AreEqual(partialImage, result.Image);
        Assert.AreEqual(ScrollingCaptureFailureReason.ScrollFailed, result.Failure?.Reason);
        Assert.ContainsSingle(result.Frames);
        Assert.AreEqual("ScrollFailed", result.Diagnostics["failureReason"]);
    }

    [TestMethod]
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

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.HasOutput);
        Assert.AreEqual(failure, result.Failure);
        Assert.AreEqual(0, stitcher.CallCount);
    }

    [TestMethod]
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

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScrollingCaptureFailureReason.InvalidRequest, result.Failure?.Reason);
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
