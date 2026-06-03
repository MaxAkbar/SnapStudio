namespace SnapStudio.Core.ScreenRecording;

public interface IScreenRecordingService
{
    Task<ScreenRecordingStartResult> StartAsync(
        ScreenRecordingStartRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingControlResult> PauseAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);

    Task<ScreenRecordingControlResult> ResumeAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);

    Task<ScreenRecordingStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);

    Task<ScreenRecordingStatusResult> GetStatusAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);
}

public interface IScreenRecordingEngine
{
    Task<ScreenRecordingStartResult> StartAsync(
        ScreenRecordingStartRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingStopResult> StopAsync(
        ScreenRecordingSession session,
        CancellationToken cancellationToken);
}

public interface IScreenRecordingFrameSource
{
    Task<ScreenRecordingFrameSourceOpenResult> OpenAsync(
        ScreenRecordingFrameSourceOpenRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingFrameSourceStartResult> StartAsync(
        ScreenRecordingFrameSourceStartRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingFrameSourceStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);
}

public interface IScreenRecordingAudioSource
{
    Task<ScreenRecordingAudioSourceOpenResult> OpenAsync(
        ScreenRecordingAudioSourceOpenRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingAudioSourceStartResult> StartAsync(
        ScreenRecordingAudioSourceStartRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingAudioSourceStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);
}

public interface IScreenRecordingFrameSink
{
    Task<ScreenRecordingFrameWriteResult> WriteFrameAsync(
        IScreenRecordingVideoFrame frame,
        CancellationToken cancellationToken);
}

public interface IScreenRecordingAudioSink
{
    Task<ScreenRecordingAudioWriteResult> WriteAudioAsync(
        IScreenRecordingAudioSample sample,
        CancellationToken cancellationToken);
}

public interface IScreenRecordingOutputWriter : IScreenRecordingFrameSink
{
    Task<ScreenRecordingOutputWriterStartResult> StartAsync(
        ScreenRecordingOutputWriterStartRequest request,
        CancellationToken cancellationToken);

    Task<ScreenRecordingOutputWriterFinishResult> FinishAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken);
}
