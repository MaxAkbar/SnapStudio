using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ScreenRecordingModelsTests
{
    [TestMethod]
    public void StartSuccess_CapturesRecordingSession()
    {
        ScreenRecordingSession session = CreateSession(ScreenRecordingState.Recording);

        ScreenRecordingStartResult result = ScreenRecordingStartResult.Success(session);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(session, result.Session);
        Assert.IsNull(result.Failure);
    }

    [TestMethod]
    public void ControlSuccess_CapturesUpdatedSessionState()
    {
        ScreenRecordingSession session = CreateSession(ScreenRecordingState.Paused);

        ScreenRecordingControlResult result = ScreenRecordingControlResult.Success(session);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ScreenRecordingState.Paused, result.Session?.State);
    }

    [TestMethod]
    public void StopSuccess_CapturesOutputAndDiagnostics()
    {
        ScreenRecordingSession session = CreateSession(ScreenRecordingState.Stopped);
        var output = new ScreenRecordingOutput(
            "recording.mp4",
            TimeSpan.FromSeconds(12),
            1920,
            1080,
            FileSizeBytes: 1024,
            ContainerFormat: "mp4",
            VideoCodec: "h264",
            AudioCodec: null);

        ScreenRecordingStopResult result = ScreenRecordingStopResult.Success(
            output,
            session,
            new Dictionary<string, string> { ["droppedFrames"] = "0" });

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.HasOutput);
        Assert.AreEqual(output, result.Output);
        Assert.AreEqual("0", result.Diagnostics["droppedFrames"]);
    }

    [TestMethod]
    public void Failure_CapturesTypedFailureWithoutOutput()
    {
        ScreenRecordingFailure failure = new(
            ScreenRecordingFailureReason.EncoderUnavailable,
            "H.264 encoder is unavailable.");

        ScreenRecordingStopResult result = ScreenRecordingStopResult.Failed(failure);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.HasOutput);
        Assert.AreEqual(failure, result.Failure);
    }

    private static ScreenRecordingSession CreateSession(ScreenRecordingState state)
    {
        return new ScreenRecordingSession(
            ScreenRecordingSessionId.New(),
            CaptureTargetKind.Display,
            DateTimeOffset.UtcNow,
            "recording.mp4",
            state,
            new Dictionary<string, string>());
    }
}
