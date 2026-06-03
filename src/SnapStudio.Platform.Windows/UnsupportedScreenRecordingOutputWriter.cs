using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScreenRecordingOutputWriter : IScreenRecordingOutputWriter
{
    public Task<ScreenRecordingOutputWriterStartResult> StartAsync(
        ScreenRecordingOutputWriterStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingOutputWriterStartResult.Failed(
            new ScreenRecordingFailure(
                ScreenRecordingFailureReason.EncoderUnavailable,
                "MP4 output writing is not implemented yet.")));
    }

    public Task<ScreenRecordingOutputWriterFinishResult> FinishAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingOutputWriterFinishResult.Failed(
            new ScreenRecordingFailure(
                ScreenRecordingFailureReason.EncoderUnavailable,
                "MP4 output writing is not implemented yet.")));
    }

    public Task<ScreenRecordingFrameWriteResult> WriteFrameAsync(
        IScreenRecordingVideoFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingFrameWriteResult.Failed(
            new ScreenRecordingFailure(
                ScreenRecordingFailureReason.EncoderUnavailable,
                "MP4 output writing is not implemented yet.")));
    }
}
