using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsWasapiScreenRecordingAudioSourceTests
{
    [TestMethod]
    public async Task OpenAsync_WhenNoAudioIsRequested_ReturnsInvalidRequest()
    {
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(),
            new FakeClock(),
            CreateOptions());

        ScreenRecordingAudioSourceOpenResult result = await source.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                ScreenRecordingSessionId.New(),
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    "recording.mp4")),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task OpenAsync_WhenMicrophoneAndSystemAudioAreRequested_ReturnsAudioUnavailable()
    {
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(),
            new FakeClock(),
            CreateOptions());

        ScreenRecordingAudioSourceOpenResult result = await source.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                ScreenRecordingSessionId.New(),
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    "recording.mp4",
                    IncludeMicrophoneAudio: true,
                    IncludeSystemAudio: true)),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task OpenAsync_WhenMicrophoneIsRequested_ReturnsAudioSessionMetadata()
    {
        var capture = new FakeCapture();
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(capture),
            new FakeClock(),
            CreateOptions());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();

        ScreenRecordingAudioSourceOpenResult result = await source.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                sessionId,
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    "recording.mp4",
                    IncludeMicrophoneAudio: true)),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(sessionId, result.Session.SessionId);
        Assert.AreEqual(48000, result.Session.SampleRate);
        Assert.AreEqual(2, result.Session.ChannelCount);
        Assert.AreEqual(16, result.Session.BitsPerSample);
        Assert.AreEqual("Pcm16", result.Session.Encoding);
        Assert.AreEqual("WindowsWasapi", result.Session.Metadata["adapter"]);
        Assert.AreEqual("Microphone", result.Session.Metadata["captureKind"]);
    }

    [TestMethod]
    public async Task OpenAsync_WhenFactoryFails_ReturnsAudioUnavailable()
    {
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(openException: new InvalidOperationException("No device.")),
            new FakeClock(),
            CreateOptions());

        ScreenRecordingAudioSourceOpenResult result = await source.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                ScreenRecordingSessionId.New(),
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    "recording.mp4",
                    IncludeSystemAudio: true)),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StartAsync_WhenAudioDataArrives_DeliversCopiedPcmSample()
    {
        var capture = new FakeCapture();
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(capture),
            new FakeClock(),
            CreateOptions());
        var sink = new CapturingAudioSink();
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await source.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                sessionId,
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    "recording.mp4",
                    IncludeSystemAudio: true)),
            CancellationToken.None);

        ScreenRecordingAudioSourceStartResult start = await source.StartAsync(
            new ScreenRecordingAudioSourceStartRequest(sessionId, sink),
            CancellationToken.None);
        byte[] buffer = Enumerable.Range(0, 960).Select(value => (byte)(value % byte.MaxValue)).ToArray();
        capture.RaiseDataAvailable(buffer, buffer.Length);
        buffer[0] = 42;
        CapturedAudioSample sample = await sink.WaitForSampleAsync();

        Assert.IsTrue(start.Succeeded);
        Assert.AreEqual(1, capture.StartRecordingCallCount);
        Assert.AreEqual(sessionId, sample.SessionId);
        Assert.AreEqual(1, sample.SequenceNumber);
        Assert.AreEqual(TimeSpan.Zero, sample.Timestamp);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5), sample.Duration);
        Assert.AreEqual(0, sample.AudioBytes[0]);
        Assert.AreEqual("SystemAudio", sample.Metadata["captureKind"]);
    }

    [TestMethod]
    public async Task StopAsync_WhenStarted_ReturnsDiagnosticsAndDisposesCapture()
    {
        var capture = new FakeCapture();
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(capture),
            new FakeClock(),
            CreateOptions());
        var sink = new CapturingAudioSink();
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await source.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                sessionId,
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    "recording.mp4",
                    IncludeMicrophoneAudio: true)),
            CancellationToken.None);
        await source.StartAsync(
            new ScreenRecordingAudioSourceStartRequest(sessionId, sink),
            CancellationToken.None);
        capture.RaiseDataAvailable(new byte[960], 960);
        await sink.WaitForSampleAsync();

        ScreenRecordingAudioSourceStopResult result = await source.StopAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, capture.StopRecordingCallCount);
        Assert.IsTrue(capture.Disposed);
        Assert.AreEqual("1", result.Diagnostics["samplesReceived"]);
        Assert.AreEqual("1", result.Diagnostics["samplesAccepted"]);
        Assert.AreEqual("960", result.Diagnostics["bytesReceived"]);
        Assert.AreEqual("Microphone", result.Diagnostics["captureKind"]);
    }

    [TestMethod]
    public async Task StartAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(),
            new FakeClock(),
            CreateOptions());

        ScreenRecordingAudioSourceStartResult result = await source.StartAsync(
            new ScreenRecordingAudioSourceStartRequest(
                ScreenRecordingSessionId.New(),
                new CapturingAudioSink()),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StopAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var source = new WindowsWasapiScreenRecordingAudioSource(
            new FakeCaptureFactory(),
            new FakeClock(),
            CreateOptions());

        ScreenRecordingAudioSourceStopResult result = await source.StopAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    private static WindowsWasapiScreenRecordingAudioSourceOptions CreateOptions()
    {
        return new WindowsWasapiScreenRecordingAudioSourceOptions(
            BufferMilliseconds: 10,
            MaxOutputChannels: 2,
            StopTimeout: TimeSpan.FromMilliseconds(100));
    }

    private sealed class FakeCaptureFactory(
        FakeCapture? capture = null,
        Exception? openException = null) : IWindowsWasapiAudioCaptureFactory
    {
        private readonly FakeCapture _capture = capture ?? new FakeCapture();

        public IWindowsWasapiAudioCapture CreateCapture(WindowsWasapiScreenRecordingAudioCaptureKind kind)
        {
            if (openException is not null)
            {
                throw openException;
            }

            return _capture;
        }
    }

    private sealed class FakeCapture : IWindowsWasapiAudioCapture
    {
        public event EventHandler<WindowsWasapiAudioDataAvailableEventArgs>? DataAvailable;

        public event EventHandler<WindowsWasapiAudioRecordingStoppedEventArgs>? RecordingStopped;

        public WindowsWasapiAudioFormat Format { get; } = new(
            SampleRate: 48000,
            ChannelCount: 2,
            BitsPerSample: 16,
            Encoding: "Pcm16",
            AverageBytesPerSecond: 192000,
            BlockAlign: 4);

        public string DeviceId => "fake-device-id";

        public string DeviceName => "Fake Audio Device";

        public int StartRecordingCallCount { get; private set; }

        public int StopRecordingCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public void StartRecording()
        {
            StartRecordingCallCount++;
        }

        public void StopRecording()
        {
            StopRecordingCallCount++;
            RecordingStopped?.Invoke(this, new WindowsWasapiAudioRecordingStoppedEventArgs(exception: null));
        }

        public void RaiseDataAvailable(byte[] buffer, int bytesRecorded)
        {
            DataAvailable?.Invoke(this, new WindowsWasapiAudioDataAvailableEventArgs(buffer, bytesRecorded));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class CapturingAudioSink : IScreenRecordingAudioSink
    {
        private readonly TaskCompletionSource<CapturedAudioSample> _sample = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ScreenRecordingAudioWriteResult> WriteAudioAsync(
            IScreenRecordingAudioSample sample,
            CancellationToken cancellationToken)
        {
            if (sample is not IWindowsScreenRecordingPcmAudioSample pcmSample)
            {
                return Task.FromResult(ScreenRecordingAudioWriteResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.EncoderUnavailable,
                    "Expected a Windows PCM sample.")));
            }

            _sample.TrySetResult(new CapturedAudioSample(
                sample.SessionId,
                sample.SequenceNumber,
                sample.Timestamp,
                sample.Duration,
                pcmSample.CopyAudioBytes(),
                sample.Metadata));

            return Task.FromResult(ScreenRecordingAudioWriteResult.Success());
        }

        public async Task<CapturedAudioSample> WaitForSampleAsync()
        {
            Task completed = await Task
                .WhenAny(_sample.Task, Task.Delay(TimeSpan.FromSeconds(2)))
                .ConfigureAwait(false);

            Assert.AreSame(_sample.Task, completed);
            return await _sample.Task.ConfigureAwait(false);
        }
    }

    private sealed record CapturedAudioSample(
        ScreenRecordingSessionId SessionId,
        long SequenceNumber,
        TimeSpan Timestamp,
        TimeSpan Duration,
        byte[] AudioBytes,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
