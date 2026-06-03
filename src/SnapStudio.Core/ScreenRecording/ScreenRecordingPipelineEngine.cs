using System.Collections.Concurrent;
using System.Globalization;
using SnapStudio.Core.System;

namespace SnapStudio.Core.ScreenRecording;

public sealed class ScreenRecordingPipelineEngine : IScreenRecordingEngine
{
    private readonly IClock _clock;
    private readonly IScreenRecordingAudioSource? _audioSource;
    private readonly IScreenRecordingFrameSource _frameSource;
    private readonly IScreenRecordingOutputWriter _outputWriter;
    private readonly ConcurrentDictionary<ScreenRecordingSessionId, PipelineRuntime> _sessions = new();

    public ScreenRecordingPipelineEngine(
        IScreenRecordingFrameSource frameSource,
        IScreenRecordingOutputWriter outputWriter,
        IClock? clock = null,
        IScreenRecordingAudioSource? audioSource = null)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _outputWriter = outputWriter ?? throw new ArgumentNullException(nameof(outputWriter));
        _clock = clock ?? new SystemClock();
        _audioSource = audioSource;
    }

    public async Task<ScreenRecordingStartResult> StartAsync(
        ScreenRecordingStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        ScreenRecordingAudioSourceSession? audioSession = null;
        IScreenRecordingAudioSink? audioSink = null;

        try
        {
            if (RequiresAudio(request))
            {
                if (_audioSource is null)
                {
                    return ScreenRecordingStartResult.Failed(CreateAudioUnavailableFailure(
                        "No screen recording audio source is configured."));
                }

                ScreenRecordingAudioSourceOpenResult audioResult = await _audioSource
                    .OpenAsync(
                        new ScreenRecordingAudioSourceOpenRequest(sessionId, request),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!audioResult.Succeeded || audioResult.Session is null)
                {
                    return ScreenRecordingStartResult.Failed(
                        audioResult.Failure ?? CreateAudioUnavailableFailure("The recording audio source failed to start."));
                }

                audioSession = audioResult.Session;
                if (_outputWriter is not IScreenRecordingAudioSink resolvedAudioSink)
                {
                    await StopAudioSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);

                    return ScreenRecordingStartResult.Failed(CreateAudioUnavailableFailure(
                        "The recording output writer does not support audio samples."));
                }

                audioSink = resolvedAudioSink;
            }

            ScreenRecordingFrameSourceOpenResult sourceResult = await _frameSource
                .OpenAsync(
                    new ScreenRecordingFrameSourceOpenRequest(sessionId, request),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!sourceResult.Succeeded || sourceResult.Session is null)
            {
                await StopAudioSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);

                return ScreenRecordingStartResult.Failed(
                    sourceResult.Failure ?? CreateUnknownFailure("The recording frame source failed to start."));
            }

            ScreenRecordingOutputWriterStartResult writerResult = await _outputWriter
                .StartAsync(
                    new ScreenRecordingOutputWriterStartRequest(sessionId, request, sourceResult.Session, audioSession),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!writerResult.Succeeded)
            {
                await StopFrameSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);
                await StopAudioSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);

                return ScreenRecordingStartResult.Failed(
                    writerResult.Failure ?? CreateUnknownFailure("The recording output writer failed to start."));
            }

            ScreenRecordingFrameSourceStartResult sourceStartResult = await _frameSource
                .StartAsync(
                    new ScreenRecordingFrameSourceStartRequest(sessionId, _outputWriter),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!sourceStartResult.Succeeded)
            {
                await FinishWriterBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);
                await StopFrameSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);
                await StopAudioSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);

                return ScreenRecordingStartResult.Failed(
                    sourceStartResult.Failure ?? CreateUnknownFailure("The recording frame source failed to start."));
            }

            if (audioSession is not null)
            {
                ScreenRecordingAudioSourceStartResult audioStartResult = await _audioSource!
                    .StartAsync(
                        new ScreenRecordingAudioSourceStartRequest(sessionId, audioSink!),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!audioStartResult.Succeeded)
                {
                    await FinishWriterBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    await StopFrameSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    await StopAudioSourceBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);

                    return ScreenRecordingStartResult.Failed(
                        audioStartResult.Failure ?? CreateAudioUnavailableFailure("The recording audio source failed to start."));
                }
            }

            var session = new ScreenRecordingSession(
                sessionId,
                request.TargetKind,
                _clock.UtcNow,
                request.OutputPath,
                ScreenRecordingState.Recording,
                CreateSessionMetadata(sourceResult.Session, audioSession, writerResult.Metadata));

            _sessions[sessionId] = new PipelineRuntime(sourceResult.Session, audioSession);

            return ScreenRecordingStartResult.Success(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await StopFrameSourceBestEffortAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            await StopAudioSourceBestEffortAsync(sessionId, CancellationToken.None).ConfigureAwait(false);

            return ScreenRecordingStartResult.Failed(new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unknown,
                "The recording pipeline failed to start.",
                exception));
        }
    }

    private async Task FinishWriterBestEffortAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _outputWriter.FinishAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    public async Task<ScreenRecordingStopResult> StopAsync(
        ScreenRecordingSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryRemove(session.Id, out PipelineRuntime? runtime))
        {
            return ScreenRecordingStopResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The recording pipeline session was not found."),
                session);
        }

        try
        {
            ScreenRecordingFrameSourceStopResult sourceStop = await _frameSource
                .StopAsync(session.Id, cancellationToken)
                .ConfigureAwait(false);
            ScreenRecordingAudioSourceStopResult? audioStop = runtime.AudioSession is not null && _audioSource is not null
                ? await _audioSource.StopAsync(session.Id, cancellationToken).ConfigureAwait(false)
                : null;
            ScreenRecordingOutputWriterFinishResult writerFinish = await _outputWriter
                .FinishAsync(session.Id, cancellationToken)
                .ConfigureAwait(false);

            Dictionary<string, string> diagnostics = CreateStopDiagnostics(
                runtime,
                sourceStop,
                audioStop,
                writerFinish);

            ScreenRecordingSession completedSession = session with
            {
                State = writerFinish.Succeeded
                    ? ScreenRecordingState.Stopped
                    : ScreenRecordingState.Failed
            };

            if (writerFinish.Succeeded && writerFinish.Output is not null)
            {
                return ScreenRecordingStopResult.Success(
                    writerFinish.Output,
                    completedSession,
                    diagnostics);
            }

            return ScreenRecordingStopResult.Failed(
                writerFinish.Failure ?? CreateUnknownFailure("The recording output writer failed to finish."),
                completedSession,
                diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ScreenRecordingStopResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.Unknown,
                    "The recording pipeline failed to stop.",
                    exception),
                session with { State = ScreenRecordingState.Failed });
        }
    }

    private async Task StopFrameSourceBestEffortAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _frameSource.StopAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    private async Task StopAudioSourceBestEffortAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (_audioSource is null)
        {
            return;
        }

        try
        {
            await _audioSource.StopAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> CreateSessionMetadata(
        ScreenRecordingFrameSourceSession sourceSession,
        ScreenRecordingAudioSourceSession? audioSession,
        IReadOnlyDictionary<string, string> writerMetadata)
    {
        var metadata = new Dictionary<string, string>
        {
            ["sourceWidth"] = sourceSession.Width.ToString(CultureInfo.InvariantCulture),
            ["sourceHeight"] = sourceSession.Height.ToString(CultureInfo.InvariantCulture),
            ["sourcePixelFormat"] = sourceSession.PixelFormat
        };

        if (sourceSession.FrameInterval is { } frameInterval)
        {
            metadata["sourceFrameIntervalMilliseconds"] = frameInterval.TotalMilliseconds
                .ToString(CultureInfo.InvariantCulture);
        }

        if (audioSession is not null)
        {
            metadata["audioSampleRate"] = audioSession.SampleRate.ToString(CultureInfo.InvariantCulture);
            metadata["audioChannelCount"] = audioSession.ChannelCount.ToString(CultureInfo.InvariantCulture);
            metadata["audioBitsPerSample"] = audioSession.BitsPerSample.ToString(CultureInfo.InvariantCulture);
            metadata["audioEncoding"] = audioSession.Encoding;

            if (audioSession.SampleInterval is { } sampleInterval)
            {
                metadata["audioSampleIntervalMilliseconds"] = sampleInterval.TotalMilliseconds
                    .ToString(CultureInfo.InvariantCulture);
            }

            AddPrefixed(metadata, "audio", audioSession.Metadata);
        }

        AddPrefixed(metadata, "source", sourceSession.Metadata);
        AddPrefixed(metadata, "writer", writerMetadata);

        return metadata;
    }

    private static Dictionary<string, string> CreateStopDiagnostics(
        PipelineRuntime runtime,
        ScreenRecordingFrameSourceStopResult sourceStop,
        ScreenRecordingAudioSourceStopResult? audioStop,
        ScreenRecordingOutputWriterFinishResult writerFinish)
    {
        var diagnostics = new Dictionary<string, string>
        {
            ["sourceWidth"] = runtime.SourceSession.Width.ToString(CultureInfo.InvariantCulture),
            ["sourceHeight"] = runtime.SourceSession.Height.ToString(CultureInfo.InvariantCulture)
        };

        if (!sourceStop.Succeeded && sourceStop.Failure is not null)
        {
            diagnostics["sourceStopFailureReason"] = sourceStop.Failure.Reason.ToString();
            diagnostics["sourceStopFailureMessage"] = sourceStop.Failure.Message;
        }

        if (audioStop is not null)
        {
            diagnostics["audioSampleRate"] = runtime.AudioSession!.SampleRate.ToString(CultureInfo.InvariantCulture);
            diagnostics["audioChannelCount"] = runtime.AudioSession.ChannelCount.ToString(CultureInfo.InvariantCulture);

            if (!audioStop.Succeeded && audioStop.Failure is not null)
            {
                diagnostics["audioStopFailureReason"] = audioStop.Failure.Reason.ToString();
                diagnostics["audioStopFailureMessage"] = audioStop.Failure.Message;
            }
        }

        if (!writerFinish.Succeeded && writerFinish.Failure is not null)
        {
            diagnostics["writerFailureReason"] = writerFinish.Failure.Reason.ToString();
            diagnostics["writerFailureMessage"] = writerFinish.Failure.Message;
        }

        AddPrefixed(diagnostics, "sourceStop", sourceStop.Diagnostics);
        if (audioStop is not null)
        {
            AddPrefixed(diagnostics, "audioStop", audioStop.Diagnostics);
        }

        AddPrefixed(diagnostics, "writerFinish", writerFinish.Diagnostics);

        return diagnostics;
    }

    private static void AddPrefixed(
        IDictionary<string, string> target,
        string prefix,
        IReadOnlyDictionary<string, string> values)
    {
        foreach ((string key, string value) in values)
        {
            target[$"{prefix}:{key}"] = value;
        }
    }

    private static ScreenRecordingFailure CreateUnknownFailure(string message)
    {
        return new ScreenRecordingFailure(ScreenRecordingFailureReason.Unknown, message);
    }

    private static ScreenRecordingFailure CreateAudioUnavailableFailure(string message)
    {
        return new ScreenRecordingFailure(ScreenRecordingFailureReason.AudioUnavailable, message);
    }

    private static bool RequiresAudio(ScreenRecordingStartRequest request)
    {
        return request.IncludeMicrophoneAudio || request.IncludeSystemAudio;
    }

    private sealed record PipelineRuntime(
        ScreenRecordingFrameSourceSession SourceSession,
        ScreenRecordingAudioSourceSession? AudioSession);
}
