using SnapStudio.Core.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class UnavailableCaptureTargetSelector : ICaptureTargetSelector
{
    public Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<CaptureTargetSelection?>(null);
    }
}
