using SnapStudio.Core.Capture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsGraphicsCaptureStillCaptureServiceTests
{
    [TestMethod]
    public async Task CaptureAsync_WhenWgcIsUnsupported_ReturnsUnsupportedFailure()
    {
        var service = new WindowsGraphicsCaptureStillCaptureService(
            capabilities: new FakeCaptureCapabilityService(StillCaptureCapability.Unsupported("WGC unavailable.")));

        CaptureOutcome outcome = await service.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual(CaptureFailureReason.Unsupported, outcome.Failure?.Reason);
        Assert.AreEqual("WGC unavailable.", outcome.Failure?.Message);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenTargetIsUnsupported_ReturnsUnsupportedFailure()
    {
        var service = new WindowsGraphicsCaptureStillCaptureService(
            capabilities: new FakeCaptureCapabilityService(StillCaptureCapability.Supported([CaptureTargetKind.Window])));

        CaptureOutcome outcome = await service.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual(CaptureFailureReason.Unsupported, outcome.Failure?.Reason);
        Assert.Contains("FullScreen", outcome.Failure?.Message ?? string.Empty);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenWgcIsSupportedWithoutSelectedTarget_ReturnsTargetUnavailable()
    {
        var service = new WindowsGraphicsCaptureStillCaptureService(
            capabilities: new FakeCaptureCapabilityService(StillCaptureCapability.Supported([CaptureTargetKind.FullScreen])));

        CaptureOutcome outcome = await service.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual(CaptureFailureReason.TargetUnavailable, outcome.Failure?.Reason);
        Assert.Contains("target", outcome.Failure?.Message ?? string.Empty);
    }

    [TestMethod]
    public async Task WindowsGraphicsCaptureCapabilityService_ReturnsCapabilityReport()
    {
        var service = new WindowsGraphicsCaptureCapabilityService();

        StillCaptureCapability capability = await service.GetCapabilityAsync(CancellationToken.None);

        if (capability.IsSupported)
        {
            Assert.Contains(CaptureTargetKind.FullScreen, capability.SupportedTargets);
            Assert.Contains(CaptureTargetKind.Window, capability.SupportedTargets);
            Assert.IsNull(capability.UnavailableReason);
        }
        else
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(capability.UnavailableReason));
            Assert.IsEmpty(capability.SupportedTargets);
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
