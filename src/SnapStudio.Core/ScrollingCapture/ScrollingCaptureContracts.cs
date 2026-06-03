namespace SnapStudio.Core.ScrollingCapture;

public interface IScrollTargetDetector
{
    Task<IReadOnlyList<ScrollTargetCandidate>> DetectAsync(
        ScrollTargetDetectionRequest request,
        CancellationToken cancellationToken);
}

public interface IScrollingCaptureService
{
    Task<ScrollingCaptureResult> CaptureAsync(
        ScrollingCaptureRequest request,
        CancellationToken cancellationToken);
}

public interface IScrollInputController
{
    Task<ScrollInputResult> ScrollAsync(
        ScrollInputRequest request,
        CancellationToken cancellationToken);
}

public interface IScrollingFrameCaptureService
{
    Task<ScrollingFrameCaptureResult> CaptureFrameAsync(
        ScrollingFrameCaptureRequest request,
        CancellationToken cancellationToken);
}

public interface IScrollingStitcher
{
    Task<ScrollingStitchResult> StitchAsync(
        ScrollingStitchRequest request,
        CancellationToken cancellationToken);
}

public interface IScrollingCaptureDiagnosticsBundleWriter
{
    Task<ScrollingCaptureDiagnosticsBundleResult> WriteAsync(
        ScrollingCaptureDiagnosticsBundleRequest request,
        CancellationToken cancellationToken);
}
