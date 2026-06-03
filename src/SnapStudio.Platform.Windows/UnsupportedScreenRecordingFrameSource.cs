using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScreenRecordingFrameSource : IScreenRecordingFrameSource
{
    public Task<ScreenRecordingFrameSourceOpenResult> OpenAsync(
        ScreenRecordingFrameSourceOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingFrameSourceOpenResult.Failed(
            new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unsupported,
                "Windows.Graphics.Capture video frame acquisition is not implemented yet.")));
    }

    public Task<ScreenRecordingFrameSourceStartResult> StartAsync(
        ScreenRecordingFrameSourceStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingFrameSourceStartResult.Failed(
            new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unsupported,
                "Windows.Graphics.Capture video frame acquisition is not implemented yet.")));
    }

    public Task<ScreenRecordingFrameSourceStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingFrameSourceStopResult.Failed(
            new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unsupported,
                "Windows.Graphics.Capture video frame acquisition is not implemented yet.")));
    }
}
