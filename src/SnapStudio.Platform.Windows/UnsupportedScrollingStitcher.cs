using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScrollingStitcher : IScrollingStitcher
{
    public Task<ScrollingStitchResult> StitchAsync(
        ScrollingStitchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScrollingStitchResult.Failed(
            new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.Unsupported,
                "Scrolling capture stitching is unavailable because no stitcher implementation is configured.")));
    }
}
