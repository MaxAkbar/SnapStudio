using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScreenRecordingService : IScreenRecordingService
{
    public Task<ScreenRecordingStartResult> StartAsync(
        ScreenRecordingStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingStartResult.Failed(CreateUnsupportedFailure()));
    }

    public Task<ScreenRecordingControlResult> PauseAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingControlResult.Failed(CreateUnsupportedFailure()));
    }

    public Task<ScreenRecordingControlResult> ResumeAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingControlResult.Failed(CreateUnsupportedFailure()));
    }

    public Task<ScreenRecordingStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingStopResult.Failed(CreateUnsupportedFailure()));
    }

    public Task<ScreenRecordingStatusResult> GetStatusAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingStatusResult.Failed(CreateUnsupportedFailure()));
    }

    private static ScreenRecordingFailure CreateUnsupportedFailure()
    {
        return new ScreenRecordingFailure(
            ScreenRecordingFailureReason.Unsupported,
            "Screen recording is unavailable because no recording implementation is configured.");
    }
}
