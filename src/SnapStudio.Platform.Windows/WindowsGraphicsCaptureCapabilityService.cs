using SnapStudio.Core.Capture;
using Windows.Graphics.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCaptureCapabilityService : IStillCaptureCapabilityService
{
    private static readonly CaptureTargetKind[] WgcTargets =
    [
        CaptureTargetKind.Region,
        CaptureTargetKind.Window,
        CaptureTargetKind.Display,
        CaptureTargetKind.FullScreen
    ];

    public Task<StillCaptureCapability> GetCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!GraphicsCaptureSession.IsSupported())
        {
            return Task.FromResult(StillCaptureCapability.Unsupported(
                "Windows.Graphics.Capture is not supported on this device or Windows configuration."));
        }

        return Task.FromResult(StillCaptureCapability.Supported(WgcTargets));
    }
}
