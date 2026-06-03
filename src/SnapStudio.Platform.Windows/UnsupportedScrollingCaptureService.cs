using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScrollingCaptureService : IScrollingCaptureService
{
    public Task<ScrollingCaptureResult> CaptureAsync(
        ScrollingCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScrollingCaptureResult.Failed(
            new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.Unsupported,
                "Scrolling capture is unavailable because no scrolling capture implementation is configured.")));
    }
}
