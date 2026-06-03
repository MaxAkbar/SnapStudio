using SnapStudio.Core.Capture;

namespace SnapStudio.Core.Tests;

public sealed class StillCaptureCapabilityTests
{
    [Fact]
    public void Supported_CreatesSupportedCapability()
    {
        StillCaptureCapability capability = StillCaptureCapability.Supported(
            [CaptureTargetKind.FullScreen, CaptureTargetKind.Window]);

        Assert.True(capability.IsSupported);
        Assert.Null(capability.UnavailableReason);
        Assert.Equal(
            [CaptureTargetKind.FullScreen, CaptureTargetKind.Window],
            capability.SupportedTargets);
    }

    [Fact]
    public void Unsupported_CreatesUnsupportedCapability()
    {
        StillCaptureCapability capability = StillCaptureCapability.Unsupported("WGC unavailable.");

        Assert.False(capability.IsSupported);
        Assert.Empty(capability.SupportedTargets);
        Assert.Equal("WGC unavailable.", capability.UnavailableReason);
    }
}
