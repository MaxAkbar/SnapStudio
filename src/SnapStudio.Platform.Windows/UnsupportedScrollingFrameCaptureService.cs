using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScrollingFrameCaptureService : IScrollingFrameCaptureService
{
    public Task<ScrollingFrameCaptureResult> CaptureFrameAsync(
        ScrollingFrameCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScrollingFrameCaptureResult.Failed(
            new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.FrameCaptureFailed,
                "Scrolling frame capture is unavailable because no frame capture implementation is configured.")));
    }
}
