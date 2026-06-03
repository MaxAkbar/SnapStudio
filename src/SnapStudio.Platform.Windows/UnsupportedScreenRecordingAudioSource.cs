using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScreenRecordingAudioSource : IScreenRecordingAudioSource
{
    public Task<ScreenRecordingAudioSourceOpenResult> OpenAsync(
        ScreenRecordingAudioSourceOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingAudioSourceOpenResult.Failed(CreateUnavailableFailure()));
    }

    public Task<ScreenRecordingAudioSourceStartResult> StartAsync(
        ScreenRecordingAudioSourceStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingAudioSourceStartResult.Failed(CreateUnavailableFailure()));
    }

    public Task<ScreenRecordingAudioSourceStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ScreenRecordingAudioSourceStopResult.Failed(CreateUnavailableFailure()));
    }

    private static ScreenRecordingFailure CreateUnavailableFailure()
    {
        return new ScreenRecordingFailure(
            ScreenRecordingFailureReason.AudioUnavailable,
            "Microphone and system audio capture are not implemented yet.");
    }
}
