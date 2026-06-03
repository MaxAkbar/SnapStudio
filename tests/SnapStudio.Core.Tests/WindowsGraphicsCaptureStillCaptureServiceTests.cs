using SnapStudio.Core.Capture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsGraphicsCaptureStillCaptureServiceTests
{
    [Fact]
    public async Task CaptureAsync_WhenWgcIsUnsupported_ReturnsUnsupportedFailure()
    {
        var service = new WindowsGraphicsCaptureStillCaptureService(
            capabilities: new FakeCaptureCapabilityService(StillCaptureCapability.Unsupported("WGC unavailable.")));

        CaptureOutcome outcome = await service.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(CaptureFailureReason.Unsupported, outcome.Failure?.Reason);
        Assert.Equal("WGC unavailable.", outcome.Failure?.Message);
    }

    [Fact]
    public async Task CaptureAsync_WhenTargetIsUnsupported_ReturnsUnsupportedFailure()
    {
        var service = new WindowsGraphicsCaptureStillCaptureService(
            capabilities: new FakeCaptureCapabilityService(StillCaptureCapability.Supported([CaptureTargetKind.Window])));

        CaptureOutcome outcome = await service.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(CaptureFailureReason.Unsupported, outcome.Failure?.Reason);
        Assert.Contains("FullScreen", outcome.Failure?.Message);
    }

    [Fact]
    public async Task CaptureAsync_WhenWgcIsSupportedWithoutSelectedTarget_ReturnsTargetUnavailable()
    {
        var service = new WindowsGraphicsCaptureStillCaptureService(
            capabilities: new FakeCaptureCapabilityService(StillCaptureCapability.Supported([CaptureTargetKind.FullScreen])));

        CaptureOutcome outcome = await service.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(CaptureFailureReason.TargetUnavailable, outcome.Failure?.Reason);
        Assert.Contains("target", outcome.Failure?.Message);
    }

    [Fact]
    public async Task WindowsGraphicsCaptureCapabilityService_ReturnsCapabilityReport()
    {
        var service = new WindowsGraphicsCaptureCapabilityService();

        StillCaptureCapability capability = await service.GetCapabilityAsync(CancellationToken.None);

        if (capability.IsSupported)
        {
            Assert.Contains(CaptureTargetKind.FullScreen, capability.SupportedTargets);
            Assert.Contains(CaptureTargetKind.Window, capability.SupportedTargets);
            Assert.Null(capability.UnavailableReason);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
            Assert.Empty(capability.SupportedTargets);
        }
    }

    private sealed class FakeCaptureCapabilityService(StillCaptureCapability capability)
        : IStillCaptureCapabilityService
    {
        public Task<StillCaptureCapability> GetCapabilityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(capability);
        }
    }
}
