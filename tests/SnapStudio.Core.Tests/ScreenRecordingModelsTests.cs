using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Core.Tests;

public sealed class ScreenRecordingModelsTests
{
    [Fact]
    public void StartSuccess_CapturesRecordingSession()
    {
        ScreenRecordingSession session = CreateSession(ScreenRecordingState.Recording);

        ScreenRecordingStartResult result = ScreenRecordingStartResult.Success(session);

        Assert.True(result.Succeeded);
        Assert.Equal(session, result.Session);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void ControlSuccess_CapturesUpdatedSessionState()
    {
        ScreenRecordingSession session = CreateSession(ScreenRecordingState.Paused);

        ScreenRecordingControlResult result = ScreenRecordingControlResult.Success(session);

        Assert.True(result.Succeeded);
        Assert.Equal(ScreenRecordingState.Paused, result.Session?.State);
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.True(result.HasOutput);
        Assert.Equal(output, result.Output);
        Assert.Equal("0", result.Diagnostics["droppedFrames"]);
    }

    [Fact]
    public void Failure_CapturesTypedFailureWithoutOutput()
    {
        ScreenRecordingFailure failure = new(
            ScreenRecordingFailureReason.EncoderUnavailable,
            "H.264 encoder is unavailable.");

        ScreenRecordingStopResult result = ScreenRecordingStopResult.Failed(failure);

        Assert.False(result.Succeeded);
        Assert.False(result.HasOutput);
        Assert.Equal(failure, result.Failure);
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
