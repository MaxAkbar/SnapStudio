using SnapStudio.Core.Capture;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class StillCaptureCapabilityTests
{
    [TestMethod]
    public void Supported_CreatesSupportedCapability()
    {
        StillCaptureCapability capability = StillCaptureCapability.Supported(
            [CaptureTargetKind.FullScreen, CaptureTargetKind.Window]);

        Assert.IsTrue(capability.IsSupported);
        Assert.IsNull(capability.UnavailableReason);
        Assert.AreSequenceEqual(
            [CaptureTargetKind.FullScreen, CaptureTargetKind.Window],
            capability.SupportedTargets);
    }

    [TestMethod]
    public void Unsupported_CreatesUnsupportedCapability()
    {
        StillCaptureCapability capability = StillCaptureCapability.Unsupported("WGC unavailable.");

        Assert.IsFalse(capability.IsSupported);
        Assert.IsEmpty(capability.SupportedTargets);
        Assert.AreEqual("WGC unavailable.", capability.UnavailableReason);
    }
}
