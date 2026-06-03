using System.Collections.Concurrent;
using System.Globalization;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsMp4ScreenRecordingOutputWriter : IScreenRecordingOutputWriter, IScreenRecordingAudioSink
{
    private readonly IClock _clock;
    private readonly IWindowsMp4Encoder _encoder;
    private readonly WindowsMp4ScreenRecordingOutputWriterOptions _options;
    private readonly ConcurrentDictionary<ScreenRecordingSessionId, WriterSession> _sessions = new();

    public WindowsMp4ScreenRecordingOutputWriter(
        IWindowsMp4Encoder? encoder = null,
        IClock? clock = null,
        WindowsMp4ScreenRecordingOutputWriterOptions? options = null)
    {
        _encoder = encoder ?? new WindowsMediaTranscoderMp4Encoder();
        _clock = clock ?? new SystemClock();
        _options = ValidateOptions(options ?? WindowsMp4ScreenRecordingOutputWriterOptions.Default);
    }

    public Task<ScreenRecordingOutputWriterStartResult> StartAsync(
        ScreenRecordingOutputWriterStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RecordingRequest);
        ArgumentNullException.ThrowIfNull(request.SourceSession);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeOutputPath(request.RecordingRequest.OutputPath, out string outputPath, out string? error))
        {
            return Task.FromResult(ScreenRecordingOutputWriterStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    error ?? "The recording output path is invalid.")));
        }

        if (request.SourceSession.Width <= 0 || request.SourceSession.Height <= 0)
        {
            return Task.FromResult(ScreenRecordingOutputWriterStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    "The recording source dimensions are invalid.")));
        }

        var session = new WriterSession(
            request.SessionId,
            outputPath,
            request.SourceSession.Width,
            request.SourceSession.Height,
            request.SourceSession.PixelFormat,
            request.AudioSession,
            _clock.UtcNow,
            _options);

        if (!_sessions.TryAdd(request.SessionId, session))
        {
            return Task.FromResult(ScreenRecordingOutputWriterStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AlreadyRecording,
                    "An MP4 writer session with the same id is already active.")));
        }

        return Task.FromResult(ScreenRecordingOutputWriterStartResult.Success(
            new Dictionary<string, string>
            {
                ["container"] = "mp4",
                ["videoCodec"] = "h264",
                ["encoderState"] = "pending",
                ["outputPath"] = outputPath,
                ["sourceWidth"] = session.Width.ToString(CultureInfo.InvariantCulture),
                ["sourceHeight"] = session.Height.ToString(CultureInfo.InvariantCulture),
                ["sourcePixelFormat"] = session.PixelFormat,
                ["audioState"] = request.AudioSession is null ? "none" : "pending"
            }));
    }

    public Task<ScreenRecordingFrameWriteResult> WriteFrameAsync(
        IScreenRecordingVideoFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(frame.SessionId, out WriterSession? session))
        {
            return Task.FromResult(ScreenRecordingFrameWriteResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The MP4 writer session was not found.")));
        }

        return Task.FromResult(session.AcceptFrame(frame));
    }

    public Task<ScreenRecordingAudioWriteResult> WriteAudioAsync(
        IScreenRecordingAudioSample sample,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(sample.SessionId, out WriterSession? session))
        {
            return Task.FromResult(ScreenRecordingAudioWriteResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The MP4 writer session was not found.")));
        }

        return Task.FromResult(session.AcceptAudio(sample));
    }

    public async Task<ScreenRecordingOutputWriterFinishResult> FinishAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryRemove(sessionId, out WriterSession? session))
        {
            return ScreenRecordingOutputWriterFinishResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The MP4 writer session was not found."));
        }

        DateTimeOffset stoppedAtUtc = _clock.UtcNow;
        WindowsMp4EncodingRequest encodingRequest = session.CreateEncodingRequest(stoppedAtUtc);
        if (session.BufferFailure is not null)
        {
            Dictionary<string, string> failedDiagnostics = session.CreateDiagnostics(stoppedAtUtc);
            return ScreenRecordingOutputWriterFinishResult.Failed(
                session.BufferFailure,
                failedDiagnostics);
        }

        WindowsMp4EncodingResult encodingResult = await _encoder
            .EncodeAsync(encodingRequest, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, string> diagnostics = session.CreateDiagnostics(stoppedAtUtc);
        AddDiagnostics(diagnostics, encodingResult.Diagnostics);

        if (encodingResult.Succeeded && encodingResult.Output is not null)
        {
            return ScreenRecordingOutputWriterFinishResult.Success(
                encodingResult.Output,
                diagnostics);
        }

        return ScreenRecordingOutputWriterFinishResult.Failed(
            encodingResult.Failure ?? new ScreenRecordingFailure(
                ScreenRecordingFailureReason.EncoderUnavailable,
                "MP4 encoding failed without a typed failure."),
            diagnostics);
    }

    private static bool TryNormalizeOutputPath(
        string outputPath,
        out string normalizedPath,
        out string? error)
    {
        normalizedPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "A recording output path is required.";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(outputPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The recording output path is invalid.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            error = "The recording output path must use the .mp4 extension.";
            return false;
        }

        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error = "The recording output directory could not be created.";
                return false;
            }
        }

        return true;
    }

    private static WindowsMp4ScreenRecordingOutputWriterOptions ValidateOptions(
        WindowsMp4ScreenRecordingOutputWriterOptions options)
    {
        if (options.MaxBufferedFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum buffered frame count must be greater than zero.");
        }

        if (options.MaxBufferedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum buffered byte count must be greater than zero.");
        }

        if (options.MaxBufferedAudioSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum buffered audio sample count must be greater than zero.");
        }

        if (options.MaxBufferedAudioBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum buffered audio byte count must be greater than zero.");
        }

        return options;
    }

    private sealed class WriterSession
    {
        private readonly object _gate = new();
        private readonly List<WindowsMp4BufferedAudioSample> _audioSamples = [];
        private readonly List<WindowsMp4BufferedFrame> _frames = [];
        private readonly WindowsMp4ScreenRecordingOutputWriterOptions _options;
        private long _audioBufferedBytes;
        private long _audioSamplesReceived;
        private long _bufferedBytes;
        private long _framesReceived;
        private TimeSpan? _firstAudioTimestamp;
        private TimeSpan? _firstFrameTimestamp;
        private TimeSpan? _lastAudioTimestamp;
        private TimeSpan? _lastFrameTimestamp;
        private ScreenRecordingFailure? _bufferFailure;

        public WriterSession(
            ScreenRecordingSessionId sessionId,
            string outputPath,
            int width,
            int height,
            string pixelFormat,
            ScreenRecordingAudioSourceSession? audioSession,
            DateTimeOffset startedAtUtc,
            WindowsMp4ScreenRecordingOutputWriterOptions options)
        {
            SessionId = sessionId;
            OutputPath = outputPath;
            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            AudioSession = audioSession;
            StartedAtUtc = startedAtUtc;
            _options = options;
        }

        public ScreenRecordingAudioSourceSession? AudioSession { get; }

        public ScreenRecordingSessionId SessionId { get; }

        public string OutputPath { get; }

        public int Width { get; }

        public int Height { get; }

        public string PixelFormat { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public ScreenRecordingFailure? BufferFailure
        {
            get
            {
                lock (_gate)
                {
                    return _bufferFailure;
                }
            }
        }

        public ScreenRecordingFrameWriteResult AcceptFrame(IScreenRecordingVideoFrame frame)
        {
            if (frame is not IWindowsScreenRecordingPixelFrame pixelFrame)
            {
                return ScreenRecordingFrameWriteResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.EncoderUnavailable,
                    "The MP4 writer requires a Windows pixel frame."));
            }

            long expectedBytes = CalculateExpectedPixelBytes(frame);
            lock (_gate)
            {
                if (_bufferFailure is not null)
                {
                    return ScreenRecordingFrameWriteResult.Failed(_bufferFailure);
                }

                ScreenRecordingFailure? capacityFailure = TryCreateCapacityFailure(expectedBytes);
                if (capacityFailure is not null)
                {
                    _bufferFailure = capacityFailure;
                    return ScreenRecordingFrameWriteResult.Failed(capacityFailure);
                }
            }

            byte[] pixelBytes;
            try
            {
                pixelBytes = pixelFrame.CopyPixelBytes();
            }
            catch (Exception exception)
            {
                return ScreenRecordingFrameWriteResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.Unknown,
                    "The MP4 writer failed to copy frame pixels.",
                    exception));
            }

            lock (_gate)
            {
                if (_bufferFailure is not null)
                {
                    return ScreenRecordingFrameWriteResult.Failed(_bufferFailure);
                }

                ScreenRecordingFailure? capacityFailure = TryCreateCapacityFailure(pixelBytes.LongLength);
                if (capacityFailure is not null)
                {
                    _bufferFailure = capacityFailure;
                    return ScreenRecordingFrameWriteResult.Failed(capacityFailure);
                }

                _framesReceived++;
                _firstFrameTimestamp ??= frame.Timestamp;
                _lastFrameTimestamp = frame.Timestamp;
                _bufferedBytes += pixelBytes.LongLength;
                _frames.Add(new WindowsMp4BufferedFrame(
                    frame.SequenceNumber,
                    frame.Timestamp,
                    frame.Width,
                    frame.Height,
                    frame.PixelFormat,
                    pixelBytes,
                    frame.Metadata));
            }

            return ScreenRecordingFrameWriteResult.Success();
        }

        public ScreenRecordingAudioWriteResult AcceptAudio(IScreenRecordingAudioSample sample)
        {
            if (AudioSession is null)
            {
                return ScreenRecordingAudioWriteResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AudioUnavailable,
                    "The MP4 writer session was not configured for audio."));
            }

            if (sample is not IWindowsScreenRecordingPcmAudioSample pcmSample)
            {
                return ScreenRecordingAudioWriteResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.EncoderUnavailable,
                    "The MP4 writer requires a Windows PCM audio sample."));
            }

            ScreenRecordingFailure? formatFailure = ValidateAudioSampleFormat(sample);
            if (formatFailure is not null)
            {
                return ScreenRecordingAudioWriteResult.Failed(formatFailure);
            }

            byte[] audioBytes;
            try
            {
                audioBytes = pcmSample.CopyAudioBytes();
            }
            catch (Exception exception)
            {
                return ScreenRecordingAudioWriteResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.Unknown,
                    "The MP4 writer failed to copy audio sample bytes.",
                    exception));
            }

            lock (_gate)
            {
                if (_bufferFailure is not null)
                {
                    return ScreenRecordingAudioWriteResult.Failed(_bufferFailure);
                }

                ScreenRecordingFailure? capacityFailure = TryCreateAudioCapacityFailure(audioBytes.LongLength);
                if (capacityFailure is not null)
                {
                    _bufferFailure = capacityFailure;
                    return ScreenRecordingAudioWriteResult.Failed(capacityFailure);
                }

                _audioSamplesReceived++;
                _firstAudioTimestamp ??= sample.Timestamp;
                _lastAudioTimestamp = sample.Timestamp;
                _audioBufferedBytes += audioBytes.LongLength;
                _audioSamples.Add(new WindowsMp4BufferedAudioSample(
                    sample.SequenceNumber,
                    sample.Timestamp,
                    sample.Duration,
                    sample.SampleRate,
                    sample.ChannelCount,
                    sample.BitsPerSample,
                    sample.Encoding,
                    audioBytes,
                    sample.Metadata));
            }

            return ScreenRecordingAudioWriteResult.Success();
        }

        public WindowsMp4EncodingRequest CreateEncodingRequest(DateTimeOffset stoppedAtUtc)
        {
            lock (_gate)
            {
                return new WindowsMp4EncodingRequest(
                    OutputPath,
                    Width,
                    Height,
                    PixelFormat,
                    StartedAtUtc,
                    stoppedAtUtc,
                    _frames.OrderBy(frame => frame.SequenceNumber).ToArray(),
                    AudioSession,
                    _audioSamples.OrderBy(sample => sample.SequenceNumber).ToArray());
            }
        }

        public Dictionary<string, string> CreateDiagnostics(DateTimeOffset stoppedAtUtc)
        {
            lock (_gate)
            {
                var diagnostics = new Dictionary<string, string>
                {
                    ["outputPath"] = OutputPath,
                    ["framesReceived"] = _framesReceived.ToString(CultureInfo.InvariantCulture),
                    ["bufferedFrames"] = _frames.Count.ToString(CultureInfo.InvariantCulture),
                    ["bufferedBytes"] = _bufferedBytes.ToString(CultureInfo.InvariantCulture),
                    ["maxBufferedFrames"] = _options.MaxBufferedFrames.ToString(CultureInfo.InvariantCulture),
                    ["maxBufferedBytes"] = _options.MaxBufferedBytes.ToString(CultureInfo.InvariantCulture),
                    ["audioSamplesReceived"] = _audioSamplesReceived.ToString(CultureInfo.InvariantCulture),
                    ["bufferedAudioSamples"] = _audioSamples.Count.ToString(CultureInfo.InvariantCulture),
                    ["bufferedAudioBytes"] = _audioBufferedBytes.ToString(CultureInfo.InvariantCulture),
                    ["maxBufferedAudioSamples"] = _options.MaxBufferedAudioSamples.ToString(CultureInfo.InvariantCulture),
                    ["maxBufferedAudioBytes"] = _options.MaxBufferedAudioBytes.ToString(CultureInfo.InvariantCulture),
                    ["width"] = Width.ToString(CultureInfo.InvariantCulture),
                    ["height"] = Height.ToString(CultureInfo.InvariantCulture),
                    ["pixelFormat"] = PixelFormat,
                    ["startedAtUtc"] = StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["stoppedAtUtc"] = stoppedAtUtc.ToString("O", CultureInfo.InvariantCulture)
                };

                if (_firstFrameTimestamp is { } firstFrameTimestamp)
                {
                    diagnostics["firstFrameTimestampMilliseconds"] = firstFrameTimestamp.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture);
                }

                if (_lastFrameTimestamp is { } lastFrameTimestamp)
                {
                    diagnostics["lastFrameTimestampMilliseconds"] = lastFrameTimestamp.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture);
                }

                if (_firstAudioTimestamp is { } firstAudioTimestamp)
                {
                    diagnostics["firstAudioTimestampMilliseconds"] = firstAudioTimestamp.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture);
                }

                if (_lastAudioTimestamp is { } lastAudioTimestamp)
                {
                    diagnostics["lastAudioTimestampMilliseconds"] = lastAudioTimestamp.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture);
                }

                if (_bufferFailure is not null)
                {
                    diagnostics["bufferFailureReason"] = _bufferFailure.Reason.ToString();
                    diagnostics["bufferFailureMessage"] = _bufferFailure.Message;
                }

                AddFrameSyncDiagnostics(diagnostics);

                return diagnostics;
            }
        }

        private void AddFrameSyncDiagnostics(Dictionary<string, string> diagnostics)
        {
            if (_frames.Count == 0)
            {
                return;
            }

            WindowsMp4BufferedFrame[] orderedFrames = _frames
                .OrderBy(frame => frame.SequenceNumber)
                .ToArray();
            WindowsMp4BufferedFrame firstFrame = orderedFrames[0];
            WindowsMp4BufferedFrame lastFrame = orderedFrames[^1];
            TimeSpan frameSpan = lastFrame.Timestamp - firstFrame.Timestamp;
            List<TimeSpan> positiveDeltas = [];
            long sequenceGapCount = 0;
            int duplicateTimestampCount = 0;
            int nonMonotonicTimestampCount = 0;

            for (int index = 1; index < orderedFrames.Length; index++)
            {
                WindowsMp4BufferedFrame previous = orderedFrames[index - 1];
                WindowsMp4BufferedFrame current = orderedFrames[index];
                long sequenceDelta = current.SequenceNumber - previous.SequenceNumber;
                if (sequenceDelta > 1)
                {
                    sequenceGapCount += sequenceDelta - 1;
                }

                TimeSpan timestampDelta = current.Timestamp - previous.Timestamp;
                if (timestampDelta > TimeSpan.Zero)
                {
                    positiveDeltas.Add(timestampDelta);
                }
                else if (timestampDelta == TimeSpan.Zero)
                {
                    duplicateTimestampCount++;
                }
                else
                {
                    nonMonotonicTimestampCount++;
                }
            }

            diagnostics["firstSequenceNumber"] = firstFrame.SequenceNumber.ToString(CultureInfo.InvariantCulture);
            diagnostics["lastSequenceNumber"] = lastFrame.SequenceNumber.ToString(CultureInfo.InvariantCulture);
            diagnostics["sequenceGapCount"] = sequenceGapCount.ToString(CultureInfo.InvariantCulture);
            diagnostics["frameSpanMilliseconds"] = Math.Max(0, frameSpan.TotalMilliseconds)
                .ToString("F3", CultureInfo.InvariantCulture);
            diagnostics["positiveFrameDeltaCount"] = positiveDeltas.Count.ToString(CultureInfo.InvariantCulture);
            diagnostics["duplicateFrameTimestampCount"] = duplicateTimestampCount.ToString(CultureInfo.InvariantCulture);
            diagnostics["nonMonotonicFrameTimestampCount"] = nonMonotonicTimestampCount.ToString(CultureInfo.InvariantCulture);

            if (positiveDeltas.Count == 0)
            {
                return;
            }

            double averageMilliseconds = positiveDeltas.Average(delta => delta.TotalMilliseconds);
            diagnostics["averageFrameIntervalMilliseconds"] = averageMilliseconds.ToString("F3", CultureInfo.InvariantCulture);
            diagnostics["minimumFrameIntervalMilliseconds"] = positiveDeltas
                .Min(delta => delta.TotalMilliseconds)
                .ToString("F3", CultureInfo.InvariantCulture);
            diagnostics["maximumFrameIntervalMilliseconds"] = positiveDeltas
                .Max(delta => delta.TotalMilliseconds)
                .ToString("F3", CultureInfo.InvariantCulture);
            diagnostics["estimatedFrameRate"] = (1000d / averageMilliseconds).ToString("F3", CultureInfo.InvariantCulture);
        }

        private ScreenRecordingFailure? TryCreateCapacityFailure(long incomingBytes)
        {
            if (_frames.Count >= _options.MaxBufferedFrames)
            {
                return new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.OutputUnavailable,
                    "The MP4 writer reached the buffered frame limit.");
            }

            if (_bufferedBytes + incomingBytes > _options.MaxBufferedBytes)
            {
                return new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.OutputUnavailable,
                    "The MP4 writer reached the buffered byte limit.");
            }

            return null;
        }

        private ScreenRecordingFailure? TryCreateAudioCapacityFailure(long incomingBytes)
        {
            if (_audioSamples.Count >= _options.MaxBufferedAudioSamples)
            {
                return new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.OutputUnavailable,
                    "The MP4 writer reached the buffered audio sample limit.");
            }

            if (_audioBufferedBytes + incomingBytes > _options.MaxBufferedAudioBytes)
            {
                return new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.OutputUnavailable,
                    "The MP4 writer reached the buffered audio byte limit.");
            }

            return null;
        }

        private ScreenRecordingFailure? ValidateAudioSampleFormat(IScreenRecordingAudioSample sample)
        {
            if (sample.SampleRate != AudioSession!.SampleRate
                || sample.ChannelCount != AudioSession.ChannelCount
                || sample.BitsPerSample != AudioSession.BitsPerSample
                || !string.Equals(sample.Encoding, AudioSession.Encoding, StringComparison.OrdinalIgnoreCase))
            {
                return new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    "The audio sample format does not match the recording audio source session.");
            }

            return null;
        }

        private static long CalculateExpectedPixelBytes(IScreenRecordingVideoFrame frame)
        {
            return Math.Max(0, frame.Width) * Math.Max(0, frame.Height) * 4L;
        }
    }

    private static void AddDiagnostics(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach ((string key, string value) in source)
        {
            target[$"encoder:{key}"] = value;
        }
    }
}
