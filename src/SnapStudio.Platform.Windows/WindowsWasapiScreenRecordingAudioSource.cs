using System.Collections.Concurrent;
using System.Globalization;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SnapStudio.Platform.Windows;

public enum WindowsWasapiScreenRecordingAudioCaptureKind
{
    Microphone,
    SystemAudio
}

public sealed record WindowsWasapiScreenRecordingAudioSourceOptions(
    int BufferMilliseconds,
    int MaxOutputChannels,
    TimeSpan StopTimeout)
{
    public static WindowsWasapiScreenRecordingAudioSourceOptions Default { get; } = new(
        BufferMilliseconds: 50,
        MaxOutputChannels: 2,
        StopTimeout: TimeSpan.FromSeconds(2));
}

public sealed record WindowsWasapiAudioFormat(
    int SampleRate,
    int ChannelCount,
    int BitsPerSample,
    string Encoding,
    int AverageBytesPerSecond,
    int BlockAlign);

public sealed class WindowsWasapiAudioDataAvailableEventArgs(
    byte[] buffer,
    int bytesRecorded) : EventArgs
{
    public byte[] Buffer { get; } = buffer;

    public int BytesRecorded { get; } = bytesRecorded;
}

public sealed class WindowsWasapiAudioRecordingStoppedEventArgs(
    Exception? exception) : EventArgs
{
    public Exception? Exception { get; } = exception;
}

public interface IWindowsWasapiAudioCapture : IDisposable
{
    event EventHandler<WindowsWasapiAudioDataAvailableEventArgs>? DataAvailable;

    event EventHandler<WindowsWasapiAudioRecordingStoppedEventArgs>? RecordingStopped;

    WindowsWasapiAudioFormat Format { get; }

    string DeviceId { get; }

    string DeviceName { get; }

    void StartRecording();

    void StopRecording();
}

public interface IWindowsWasapiAudioCaptureFactory
{
    IWindowsWasapiAudioCapture CreateCapture(WindowsWasapiScreenRecordingAudioCaptureKind kind);
}

// NAudio 3.1 still supports these event-based capture types. Keep this compatibility
// adapter on them until a recorder migration can be validated with real audio devices.
#pragma warning disable CS0618
public sealed class WindowsWasapiAudioCaptureFactory : IWindowsWasapiAudioCaptureFactory
{
    private readonly WindowsWasapiScreenRecordingAudioSourceOptions _options;

    public WindowsWasapiAudioCaptureFactory(
        WindowsWasapiScreenRecordingAudioSourceOptions? options = null)
    {
        _options = ValidateOptions(options ?? WindowsWasapiScreenRecordingAudioSourceOptions.Default);
    }

    public IWindowsWasapiAudioCapture CreateCapture(WindowsWasapiScreenRecordingAudioCaptureKind kind)
    {
        MMDevice? device = null;
        WasapiCapture? capture = null;

        try
        {
            device = CreateDevice(kind);
            capture = kind == WindowsWasapiScreenRecordingAudioCaptureKind.Microphone
                ? new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: _options.BufferMilliseconds)
                : new WasapiLoopbackCapture(device);
            capture.ShareMode = AudioClientShareMode.Shared;
            capture.WaveFormat = CreateOutputFormat(capture.WaveFormat);

            return new NAudioWasapiAudioCapture(capture, device);
        }
        catch
        {
            capture?.Dispose();
            device?.Dispose();
            throw;
        }
    }

    private static MMDevice CreateDevice(WindowsWasapiScreenRecordingAudioCaptureKind kind)
    {
        return kind == WindowsWasapiScreenRecordingAudioCaptureKind.Microphone
            ? WasapiCapture.GetDefaultCaptureDevice()
            : WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
    }

    private WaveFormat CreateOutputFormat(WaveFormat sourceFormat)
    {
        int sampleRate = sourceFormat.SampleRate > 0 ? sourceFormat.SampleRate : 48000;
        int sourceChannels = sourceFormat.Channels > 0 ? sourceFormat.Channels : 1;
        int channelCount = Math.Clamp(sourceChannels, 1, _options.MaxOutputChannels);

        return new WaveFormat(sampleRate, bits: 16, channels: channelCount);
    }

    private static WindowsWasapiScreenRecordingAudioSourceOptions ValidateOptions(
        WindowsWasapiScreenRecordingAudioSourceOptions options)
    {
        if (options.BufferMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The WASAPI audio buffer length must be greater than zero.");
        }

        if (options.MaxOutputChannels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The WASAPI audio channel limit must be greater than zero.");
        }

        if (options.StopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The WASAPI audio stop timeout must be greater than zero.");
        }

        return options;
    }

    private sealed class NAudioWasapiAudioCapture : IWindowsWasapiAudioCapture
    {
        private readonly WasapiCapture _capture;
        private readonly MMDevice _device;

        public NAudioWasapiAudioCapture(
            WasapiCapture capture,
            MMDevice device)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _device = device ?? throw new ArgumentNullException(nameof(device));
            Format = CreateFormat(capture.WaveFormat);
            DeviceId = device.ID;
            DeviceName = device.FriendlyName;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }

        public event EventHandler<WindowsWasapiAudioDataAvailableEventArgs>? DataAvailable;

        public event EventHandler<WindowsWasapiAudioRecordingStoppedEventArgs>? RecordingStopped;

        public WindowsWasapiAudioFormat Format { get; }

        public string DeviceId { get; }

        public string DeviceName { get; }

        public void StartRecording()
        {
            _capture.StartRecording();
        }

        public void StopRecording()
        {
            _capture.StopRecording();
        }

        public void Dispose()
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _device.Dispose();
        }

        private static WindowsWasapiAudioFormat CreateFormat(WaveFormat waveFormat)
        {
            return new WindowsWasapiAudioFormat(
                waveFormat.SampleRate,
                waveFormat.Channels,
                waveFormat.BitsPerSample,
                "Pcm16",
                waveFormat.AverageBytesPerSecond,
                waveFormat.BlockAlign);
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            DataAvailable?.Invoke(
                this,
                new WindowsWasapiAudioDataAvailableEventArgs(args.Buffer, args.BytesRecorded));
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            RecordingStopped?.Invoke(
                this,
                new WindowsWasapiAudioRecordingStoppedEventArgs(args.Exception));
        }
    }
}
#pragma warning restore CS0618

public sealed class WindowsWasapiScreenRecordingAudioSource : IScreenRecordingAudioSource
{
    private readonly IWindowsWasapiAudioCaptureFactory _captureFactory;
    private readonly IClock _clock;
    private readonly WindowsWasapiScreenRecordingAudioSourceOptions _options;
    private readonly ConcurrentDictionary<ScreenRecordingSessionId, WasapiAudioRuntime> _sessions = new();

    public WindowsWasapiScreenRecordingAudioSource(
        IWindowsWasapiAudioCaptureFactory? captureFactory = null,
        IClock? clock = null,
        WindowsWasapiScreenRecordingAudioSourceOptions? options = null)
    {
        _options = ValidateOptions(options ?? WindowsWasapiScreenRecordingAudioSourceOptions.Default);
        _captureFactory = captureFactory ?? new WindowsWasapiAudioCaptureFactory(_options);
        _clock = clock ?? new SystemClock();
    }

    public Task<ScreenRecordingAudioSourceOpenResult> OpenAsync(
        ScreenRecordingAudioSourceOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RecordingRequest);
        cancellationToken.ThrowIfCancellationRequested();

        ScreenRecordingFailure? requestFailure = ValidateRequest(request.RecordingRequest);
        if (requestFailure is not null)
        {
            return Task.FromResult(ScreenRecordingAudioSourceOpenResult.Failed(requestFailure));
        }

        WindowsWasapiScreenRecordingAudioCaptureKind kind = request.RecordingRequest.IncludeMicrophoneAudio
            ? WindowsWasapiScreenRecordingAudioCaptureKind.Microphone
            : WindowsWasapiScreenRecordingAudioCaptureKind.SystemAudio;

        try
        {
            IWindowsWasapiAudioCapture capture = _captureFactory.CreateCapture(kind);
            var runtime = new WasapiAudioRuntime(
                request.SessionId,
                kind,
                capture,
                _clock.UtcNow,
                _options);

            if (!_sessions.TryAdd(request.SessionId, runtime))
            {
                runtime.Dispose();

                return Task.FromResult(ScreenRecordingAudioSourceOpenResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AlreadyRecording,
                    "A WASAPI audio source session with the same id is already active.")));
            }

            WindowsWasapiAudioFormat format = capture.Format;
            return Task.FromResult(ScreenRecordingAudioSourceOpenResult.Success(
                new ScreenRecordingAudioSourceSession(
                    request.SessionId,
                    format.SampleRate,
                    format.ChannelCount,
                    format.BitsPerSample,
                    format.Encoding,
                    TimeSpan.FromMilliseconds(_options.BufferMilliseconds),
                    CreateMetadata(kind, capture, format, request.RecordingRequest))));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(ScreenRecordingAudioSourceOpenResult.Failed(new ScreenRecordingFailure(
                ScreenRecordingFailureReason.AudioUnavailable,
                "WASAPI audio capture could not be opened.",
                exception)));
        }
    }

    public Task<ScreenRecordingAudioSourceStartResult> StartAsync(
        ScreenRecordingAudioSourceStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AudioSink);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(request.SessionId, out WasapiAudioRuntime? runtime))
        {
            return Task.FromResult(ScreenRecordingAudioSourceStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The WASAPI audio source session was not found.")));
        }

        try
        {
            runtime.Start(request.AudioSink);

            return Task.FromResult(ScreenRecordingAudioSourceStartResult.Success(
                new Dictionary<string, string>
                {
                    ["audioStartedAtUtc"] = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                }));
        }
        catch (Exception exception)
        {
            _sessions.TryRemove(request.SessionId, out _);
            runtime.Dispose();

            return Task.FromResult(ScreenRecordingAudioSourceStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AudioUnavailable,
                    "WASAPI audio capture could not be started.",
                    exception)));
        }
    }

    public async Task<ScreenRecordingAudioSourceStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryRemove(sessionId, out WasapiAudioRuntime? runtime))
        {
            return ScreenRecordingAudioSourceStopResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The WASAPI audio source session was not found."));
        }

        return await runtime
            .StopAsync(_clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string> CreateMetadata(
        WindowsWasapiScreenRecordingAudioCaptureKind kind,
        IWindowsWasapiAudioCapture capture,
        WindowsWasapiAudioFormat format,
        ScreenRecordingStartRequest request)
    {
        return new Dictionary<string, string>
        {
            ["adapter"] = "WindowsWasapi",
            ["captureKind"] = kind.ToString(),
            ["deviceId"] = capture.DeviceId,
            ["deviceName"] = capture.DeviceName,
            ["requestedMicrophone"] = request.IncludeMicrophoneAudio.ToString(),
            ["requestedSystemAudio"] = request.IncludeSystemAudio.ToString(),
            ["sampleRate"] = format.SampleRate.ToString(CultureInfo.InvariantCulture),
            ["channelCount"] = format.ChannelCount.ToString(CultureInfo.InvariantCulture),
            ["bitsPerSample"] = format.BitsPerSample.ToString(CultureInfo.InvariantCulture),
            ["encoding"] = format.Encoding,
            ["averageBytesPerSecond"] = format.AverageBytesPerSecond.ToString(CultureInfo.InvariantCulture),
            ["blockAlign"] = format.BlockAlign.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static ScreenRecordingFailure? ValidateRequest(ScreenRecordingStartRequest request)
    {
        bool requestedMicrophone = request.IncludeMicrophoneAudio;
        bool requestedSystemAudio = request.IncludeSystemAudio;

        if (!requestedMicrophone && !requestedSystemAudio)
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.InvalidRequest,
                "WASAPI audio capture requires microphone or system audio.");
        }

        if (requestedMicrophone && requestedSystemAudio)
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.AudioUnavailable,
                "Recording microphone and system audio together requires mixing and is not implemented yet.");
        }

        return null;
    }

    private static WindowsWasapiScreenRecordingAudioSourceOptions ValidateOptions(
        WindowsWasapiScreenRecordingAudioSourceOptions options)
    {
        if (options.BufferMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The WASAPI audio buffer length must be greater than zero.");
        }

        if (options.MaxOutputChannels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The WASAPI audio channel limit must be greater than zero.");
        }

        if (options.StopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The WASAPI audio stop timeout must be greater than zero.");
        }

        return options;
    }

    private sealed class WasapiAudioRuntime : IDisposable
    {
        private readonly object _timingGate = new();
        private readonly IWindowsWasapiAudioCapture _capture;
        private readonly string _deviceId;
        private readonly string _deviceName;
        private readonly WindowsWasapiAudioFormat _format;
        private readonly WindowsWasapiScreenRecordingAudioCaptureKind _kind;
        private readonly WindowsWasapiScreenRecordingAudioSourceOptions _options;
        private readonly TaskCompletionSource _recordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;
        private int _started;
        private long _samplesAccepted;
        private long _samplesReceived;
        private long _sampleWriteFailures;
        private long _totalBytesReceived;
        private TimeSpan _nextTimestamp = TimeSpan.Zero;
        private ScreenRecordingFailure? _recordingStoppedFailure;
        private IScreenRecordingAudioSink? _audioSink;

        public WasapiAudioRuntime(
            ScreenRecordingSessionId sessionId,
            WindowsWasapiScreenRecordingAudioCaptureKind kind,
            IWindowsWasapiAudioCapture capture,
            DateTimeOffset openedAtUtc,
            WindowsWasapiScreenRecordingAudioSourceOptions options)
        {
            SessionId = sessionId;
            _kind = kind;
            _capture = capture;
            _format = capture.Format;
            _deviceId = capture.DeviceId;
            _deviceName = capture.DeviceName;
            OpenedAtUtc = openedAtUtc;
            _options = options;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }

        public ScreenRecordingSessionId SessionId { get; }

        public DateTimeOffset OpenedAtUtc { get; }

        public void Start(IScreenRecordingAudioSink audioSink)
        {
            ArgumentNullException.ThrowIfNull(audioSink);

            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                throw new InvalidOperationException("The WASAPI audio source already started.");
            }

            _audioSink = audioSink;
            _capture.StartRecording();
        }

        public async Task<ScreenRecordingAudioSourceStopResult> StopAsync(
            DateTimeOffset stoppedAtUtc,
            CancellationToken cancellationToken)
        {
            ScreenRecordingFailure? stopFailure = null;

            try
            {
                if (Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _started) == 1)
                {
                    _capture.StopRecording();
                    await WaitForRecordingStoppedAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                stopFailure = new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AudioUnavailable,
                    "WASAPI audio capture could not be stopped.",
                    exception);
            }
            finally
            {
                Dispose();
            }

            Dictionary<string, string> diagnostics = CreateDiagnostics(stoppedAtUtc);
            ScreenRecordingFailure? failure = stopFailure ?? _recordingStoppedFailure;
            return failure is null
                ? ScreenRecordingAudioSourceStopResult.Success(diagnostics)
                : ScreenRecordingAudioSourceStopResult.Failed(failure, diagnostics);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
        }

        private async Task WaitForRecordingStoppedAsync(CancellationToken cancellationToken)
        {
            Task completed = await Task
                .WhenAny(
                    _recordingStopped.Task,
                    Task.Delay(_options.StopTimeout, cancellationToken))
                .ConfigureAwait(false);
            if (completed != _recordingStopped.Task)
            {
                _recordingStoppedFailure = new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AudioUnavailable,
                    "WASAPI audio capture did not stop before the timeout elapsed.");
            }

            await completed.ConfigureAwait(false);
        }

        private void OnDataAvailable(object? sender, WindowsWasapiAudioDataAvailableEventArgs args)
        {
            if (Volatile.Read(ref _disposed) == 1 || args.BytesRecorded <= 0)
            {
                return;
            }

            IScreenRecordingAudioSink? audioSink = _audioSink;
            if (audioSink is null)
            {
                return;
            }

            byte[] audioBytes = new byte[args.BytesRecorded];
            Buffer.BlockCopy(args.Buffer, 0, audioBytes, 0, args.BytesRecorded);

            long sequenceNumber = Interlocked.Increment(ref _samplesReceived);
            Interlocked.Add(ref _totalBytesReceived, args.BytesRecorded);

            TimeSpan duration = CalculateDuration(args.BytesRecorded, _format.AverageBytesPerSecond);
            TimeSpan timestamp;
            lock (_timingGate)
            {
                timestamp = _nextTimestamp;
                _nextTimestamp += duration;
            }

            var sample = new WindowsWasapiScreenRecordingAudioSample(
                SessionId,
                sequenceNumber,
                timestamp,
                duration,
                _format,
                audioBytes,
                new Dictionary<string, string>
                {
                    ["captureKind"] = _kind.ToString(),
                    ["deviceId"] = _deviceId,
                    ["deviceName"] = _deviceName
                });

            _ = DeliverAudioAsync(audioSink, sample);
        }

        private void OnRecordingStopped(object? sender, WindowsWasapiAudioRecordingStoppedEventArgs args)
        {
            if (args.Exception is not null)
            {
                _recordingStoppedFailure = new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AudioUnavailable,
                    "WASAPI audio capture stopped unexpectedly.",
                    args.Exception);
            }

            _recordingStopped.TrySetResult();
        }

        private async Task DeliverAudioAsync(
            IScreenRecordingAudioSink audioSink,
            WindowsWasapiScreenRecordingAudioSample sample)
        {
            try
            {
                ScreenRecordingAudioWriteResult result = await audioSink
                    .WriteAudioAsync(sample, CancellationToken.None)
                    .ConfigureAwait(false);

                if (result.Succeeded)
                {
                    Interlocked.Increment(ref _samplesAccepted);
                }
                else
                {
                    Interlocked.Increment(ref _sampleWriteFailures);
                }
            }
            catch
            {
                Interlocked.Increment(ref _sampleWriteFailures);
            }
            finally
            {
                sample.Dispose();
            }
        }

        private Dictionary<string, string> CreateDiagnostics(DateTimeOffset stoppedAtUtc)
        {
            return new Dictionary<string, string>
            {
                ["adapter"] = "WindowsWasapi",
                ["captureKind"] = _kind.ToString(),
                ["deviceId"] = _deviceId,
                ["deviceName"] = _deviceName,
                ["samplesReceived"] = Volatile.Read(ref _samplesReceived).ToString(CultureInfo.InvariantCulture),
                ["samplesAccepted"] = Volatile.Read(ref _samplesAccepted).ToString(CultureInfo.InvariantCulture),
                ["sampleWriteFailures"] = Volatile.Read(ref _sampleWriteFailures).ToString(CultureInfo.InvariantCulture),
                ["bytesReceived"] = Volatile.Read(ref _totalBytesReceived).ToString(CultureInfo.InvariantCulture),
                ["sampleRate"] = _format.SampleRate.ToString(CultureInfo.InvariantCulture),
                ["channelCount"] = _format.ChannelCount.ToString(CultureInfo.InvariantCulture),
                ["bitsPerSample"] = _format.BitsPerSample.ToString(CultureInfo.InvariantCulture),
                ["encoding"] = _format.Encoding,
                ["openedAtUtc"] = OpenedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["stoppedAtUtc"] = stoppedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            };
        }

        private static TimeSpan CalculateDuration(
            int byteCount,
            int averageBytesPerSecond)
        {
            if (averageBytesPerSecond <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(byteCount / (double)averageBytesPerSecond);
        }
    }
}

public sealed class WindowsWasapiScreenRecordingAudioSample : IWindowsScreenRecordingPcmAudioSample
{
    private byte[]? _audioBytes;

    public WindowsWasapiScreenRecordingAudioSample(
        ScreenRecordingSessionId sessionId,
        long sequenceNumber,
        TimeSpan timestamp,
        TimeSpan duration,
        WindowsWasapiAudioFormat format,
        byte[] audioBytes,
        IReadOnlyDictionary<string, string> metadata)
    {
        SessionId = sessionId;
        SequenceNumber = sequenceNumber;
        Timestamp = timestamp;
        Duration = duration;
        SampleRate = format.SampleRate;
        ChannelCount = format.ChannelCount;
        BitsPerSample = format.BitsPerSample;
        Encoding = format.Encoding;
        _audioBytes = audioBytes;
        Metadata = metadata;
    }

    public ScreenRecordingSessionId SessionId { get; }

    public long SequenceNumber { get; }

    public TimeSpan Timestamp { get; }

    public TimeSpan Duration { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public int BitsPerSample { get; }

    public string Encoding { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public byte[] CopyAudioBytes()
    {
        if (_audioBytes is null)
        {
            throw new ObjectDisposedException(nameof(WindowsWasapiScreenRecordingAudioSample));
        }

        return _audioBytes.ToArray();
    }

    public void Dispose()
    {
        _audioBytes = null;
    }
}
