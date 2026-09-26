using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsGraphicsCaptureScreenRecordingFrameSourceTests
{
    [TestMethod]
    public async Task OpenAsync_WhenCapabilityIsUnsupported_ReturnsUnsupported()
    {
        var frameSource = new WindowsGraphicsCaptureScreenRecordingFrameSource(
            capabilities: new FakeCaptureCapabilityService(
                StillCaptureCapability.Unsupported("WGC unavailable.")));

        ScreenRecordingFrameSourceOpenResult result = await frameSource.OpenAsync(
            CreateOpenRequest(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.Unsupported, result.Failure?.Reason);
        Assert.AreEqual("WGC unavailable.", result.Failure?.Message);
    }

    [TestMethod]
    public async Task OpenAsync_WhenTargetKindIsUnsupported_ReturnsUnsupported()
    {
        var frameSource = new WindowsGraphicsCaptureScreenRecordingFrameSource(
            capabilities: new FakeCaptureCapabilityService(
                StillCaptureCapability.Supported([CaptureTargetKind.Window])));

        ScreenRecordingFrameSourceOpenResult result = await frameSource.OpenAsync(
            CreateOpenRequest(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.Unsupported, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task OpenAsync_WhenTargetHintIsMissing_ReturnsTargetUnavailable()
    {
        var frameSource = new WindowsGraphicsCaptureScreenRecordingFrameSource(
            registry: new WindowsGraphicsCaptureItemRegistry(),
            capabilities: new FakeCaptureCapabilityService(
                StillCaptureCapability.Supported([CaptureTargetKind.Display])));

        ScreenRecordingFrameSourceOpenResult result = await frameSource.OpenAsync(
            CreateOpenRequest(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.TargetUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StopAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var frameSource = new WindowsGraphicsCaptureScreenRecordingFrameSource(
            capabilities: new FakeCaptureCapabilityService(
                StillCaptureCapability.Unsupported("WGC unavailable.")));

        ScreenRecordingFrameSourceStopResult result = await frameSource.StopAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StartAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var frameSource = new WindowsGraphicsCaptureScreenRecordingFrameSource(
            capabilities: new FakeCaptureCapabilityService(
                StillCaptureCapability.Supported([CaptureTargetKind.Display])));

        ScreenRecordingFrameSourceStartResult result = await frameSource.StartAsync(
            new ScreenRecordingFrameSourceStartRequest(
                ScreenRecordingSessionId.New(),
                new FakeFrameSink()),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    private static ScreenRecordingFrameSourceOpenRequest CreateOpenRequest()
    {
        return new ScreenRecordingFrameSourceOpenRequest(
            ScreenRecordingSessionId.New(),
            new ScreenRecordingStartRequest(
                CaptureTargetKind.Display,
                "recording.mp4",
                TargetHint: "missing-target"));
    }

    private sealed class FakeCaptureCapabilityService(
        StillCaptureCapability capability) : IStillCaptureCapabilityService
    {
        public Task<StillCaptureCapability> GetCapabilityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(capability);
        }
    }

    private sealed class FakeFrameSink : IScreenRecordingFrameSink
    {
        public Task<ScreenRecordingFrameWriteResult> WriteFrameAsync(
            IScreenRecordingVideoFrame frame,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ScreenRecordingFrameWriteResult.Success());
        }
    }
}
