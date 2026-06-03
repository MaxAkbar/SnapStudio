using SnapStudio.Core.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class UnavailableStillCaptureService : IStillCaptureService
{
    public Task<CaptureOutcome> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var failure = new CaptureFailure(
            CaptureFailureReason.NotImplemented,
            "Windows still capture is unavailable because no capture adapter is configured.");

        return Task.FromResult(CaptureOutcome.Failed(failure));
    }
}
