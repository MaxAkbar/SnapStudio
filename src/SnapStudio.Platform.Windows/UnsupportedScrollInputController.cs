using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScrollInputController : IScrollInputController
{
    public Task<ScrollInputResult> ScrollAsync(
        ScrollInputRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScrollInputResult.Failed(
            new ScrollInputFailure(
                ScrollInputFailureReason.Unsupported,
                "Controlled scrolling is unavailable because no scroll input controller is configured.")));
    }
}
