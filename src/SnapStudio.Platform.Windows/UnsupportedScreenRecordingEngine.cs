using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScreenRecordingEngine : IScreenRecordingEngine
{
    public Task<ScreenRecordingStartResult> StartAsync(
        ScreenRecordingStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingStartResult.Failed(CreateUnsupportedFailure()));
    }

    public Task<ScreenRecordingStopResult> StopAsync(
        ScreenRecordingSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingStopResult.Failed(CreateUnsupportedFailure(), session));
    }

    private static ScreenRecordingFailure CreateUnsupportedFailure()
    {
        return new ScreenRecordingFailure(
            ScreenRecordingFailureReason.Unsupported,
            "Screen recording is unavailable because no recording engine is configured.");
    }
}
