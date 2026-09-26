using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ScreenRecordingPipelineEngineTests
{
    [TestMethod]
    public async Task StartAsync_WhenAudioIsRequested_ReturnsAudioUnavailableWithoutOpeningSource()
    {
        var frameSource = new FakeFrameSource();
        var writer = new FakeOutputWriter();
        var engine = new ScreenRecordingPipelineEngine(frameSource, writer, new FakeClock());

        ScreenRecordingStartResult result = await engine.StartAsync(
            CreateRequest(includeMicrophoneAudio: true),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
        Assert.AreEqual(0, frameSource.OpenCallCount);
        Assert.AreEqual(0, writer.StartCallCount);
    }

    [TestMethod]
    public async Task StartAsync_WhenAudioSourceFails_ReturnsAudioFailureWithoutOpeningFrameSource()
    {
        var audioFailure = new ScreenRecordingFailure(
            ScreenRecordingFailureReason.AudioUnavailable,
            "No audio device.");
        var audioSource = new FakeAudioSource(
            openResult: ScreenRecordingAudioSourceOpenResult.Failed(audioFailure));
        var frameSource = new FakeFrameSource();
        var writer = new FakeOutputWriter();
        var engine = new ScreenRecordingPipelineEngine(
            frameSource,
            writer,
            new FakeClock(),
            audioSource);

        ScreenRecordingStartResult result = await engine.StartAsync(
            CreateRequest(includeSystemAudio: true),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(audioFailure, result.Failure);
        Assert.AreEqual(1, audioSource.OpenCallCount);
        Assert.AreEqual(0, frameSource.OpenCallCount);
        Assert.AreEqual(0, writer.StartCallCount);
    }

    [TestMethod]
    public async Task StartAndStopAsync_WhenAudioPipelineIsAvailable_ReportsAudioMetadataAndDiagnostics()
    {
        var audioSource = new FakeAudioSource();
        var frameSource = new FakeFrameSource();
        var writer = new FakeAudioOutputWriter();
        var engine = new ScreenRecordingPipelineEngine(
            frameSource,
            writer,
            new FakeClock(),
            audioSource);

        ScreenRecordingStartResult start = await engine.StartAsync(
            CreateRequest(includeMicrophoneAudio: true),
            CancellationToken.None);

        Assert.IsTrue(start.Succeeded);
        Assert.AreEqual("48000", start.Session?.Metadata["audioSampleRate"]);
        Assert.AreEqual("2", start.Session?.Metadata["audioChannelCount"]);
        Assert.AreEqual("Pcm16", start.Session?.Metadata["audioEncoding"]);
        Assert.AreEqual("fake-audio", start.Session?.Metadata["audio:adapter"]);
        Assert.IsNotNull(writer.LastStartRequest?.AudioSession);
        Assert.AreEqual(1, audioSource.OpenCallCount);
        Assert.AreEqual(1, audioSource.StartCallCount);

        ScreenRecordingStopResult stop = await engine.StopAsync(
            start.Session!,
            CancellationToken.None);

        Assert.IsTrue(stop.Succeeded);
        Assert.AreEqual(1, audioSource.StopCallCount);
        Assert.AreEqual("48000", stop.Diagnostics["audioSampleRate"]);
        Assert.AreEqual("2", stop.Diagnostics["audioChannelCount"]);
        Assert.AreEqual("480", stop.Diagnostics["audioStop:samplesCaptured"]);
    }

    [TestMethod]
    public async Task StartAsync_WhenFrameSourceFails_ReturnsSourceFailureWithoutStartingWriter()
    {
        var sourceFailure = new ScreenRecordingFailure(
            ScreenRecordingFailureReason.TargetUnavailable,
            "No target.");
        var frameSource = new FakeFrameSource(ScreenRecordingFrameSourceOpenResult.Failed(sourceFailure));
        var writer = new FakeOutputWriter();
        var engine = new ScreenRecordingPipelineEngine(frameSource, writer, new FakeClock());

        ScreenRecordingStartResult result = await engine.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(sourceFailure, result.Failure);
        Assert.AreEqual(0, writer.StartCallCount);
    }

    [TestMethod]
    public async Task StartAsync_WhenWriterFails_StopsFrameSourceAndReturnsWriterFailure()
    {
        var writerFailure = new ScreenRecordingFailure(
            ScreenRecordingFailureReason.EncoderUnavailable,
            "No encoder.");
        var frameSource = new FakeFrameSource();
        var writer = new FakeOutputWriter(ScreenRecordingOutputWriterStartResult.Failed(writerFailure));
        var engine = new ScreenRecordingPipelineEngine(frameSource, writer, new FakeClock());

        ScreenRecordingStartResult result = await engine.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(writerFailure, result.Failure);
        Assert.AreEqual(1, frameSource.StopCallCount);
    }

    [TestMethod]
    public async Task StartAsync_WhenFrameSourceStartFails_FinishesWriterAndStopsFrameSource()
    {
        var sourceFailure = new ScreenRecordingFailure(
            ScreenRecordingFailureReason.TargetUnavailable,
            "Lost target.");
        var frameSource = new FakeFrameSource(
            startResult: ScreenRecordingFrameSourceStartResult.Failed(sourceFailure));
        var writer = new FakeOutputWriter();
        var engine = new ScreenRecordingPipelineEngine(frameSource, writer, new FakeClock());

        ScreenRecordingStartResult result = await engine.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(sourceFailure, result.Failure);
        Assert.AreEqual(1, writer.StartCallCount);
        Assert.AreEqual(1, writer.FinishCallCount);
        Assert.AreEqual(1, frameSource.StopCallCount);
    }

    [TestMethod]
    public async Task StartAsync_WhenPipelineStarts_ReturnsRecordingSessionWithSourceMetadata()
    {
        var frameSource = new FakeFrameSource();
        var writer = new FakeOutputWriter();
        var engine = new ScreenRecordingPipelineEngine(frameSource, writer, new FakeClock());

        ScreenRecordingStartResult result = await engine.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ScreenRecordingState.Recording, result.Session?.State);
        Assert.AreEqual(new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero), result.Session?.StartedAtUtc);
        Assert.AreEqual("1920", result.Session?.Metadata["sourceWidth"]);
        Assert.AreEqual("1080", result.Session?.Metadata["sourceHeight"]);
        Assert.AreEqual("Bgra32", result.Session?.Metadata["sourcePixelFormat"]);
        Assert.AreEqual("wgc", result.Session?.Metadata["source:adapter"]);
        Assert.AreEqual("mp4", result.Session?.Metadata["writer:container"]);
    }

    [TestMethod]
    public async Task StopAsync_WhenPipelineFinishes_ReturnsOutputAndDiagnostics()
    {
        var frameSource = new FakeFrameSource(
            stopResult: ScreenRecordingFrameSourceStopResult.Success(
                new Dictionary<string, string> { ["framesReceived"] = "90" }));
        var writer = new FakeOutputWriter(
            finishResult: ScreenRecordingOutputWriterFinishResult.Success(
                CreateOutput(),
                new Dictionary<string, string> { ["framesWritten"] = "90" }));
        var engine = new ScreenRecordingPipelineEngine(frameSource, writer, new FakeClock());
        ScreenRecordingStartResult start = await engine.StartAsync(CreateRequest(), CancellationToken.None);

        ScreenRecordingStopResult stop = await engine.StopAsync(
            start.Session!,
            CancellationToken.None);

        Assert.IsTrue(stop.Succeeded);
        Assert.AreEqual(ScreenRecordingState.Stopped, stop.Session?.State);
        Assert.AreEqual("90", stop.Diagnostics["sourceStop:framesReceived"]);
        Assert.AreEqual("90", stop.Diagnostics["writerFinish:framesWritten"]);
        Assert.AreEqual(1, frameSource.StopCallCount);
        Assert.AreEqual(1, writer.FinishCallCount);
    }

    [TestMethod]
    public async Task StopAsync_WhenWriterFails_ReturnsFailedSessionWithDiagnostics()
    {
        var writerFailure = new ScreenRecordingFailure(
            ScreenRecordingFailureReason.OutputUnavailable,
            "Cannot finalize output.");
        var writer = new FakeOutputWriter(
            finishResult: ScreenRecordingOutputWriterFinishResult.Failed(
                writerFailure,
                new Dictionary<string, string> { ["framesWritten"] = "8" }));
        var engine = new ScreenRecordingPipelineEngine(
            new FakeFrameSource(),
            writer,
            new FakeClock());
        ScreenRecordingStartResult start = await engine.StartAsync(CreateRequest(), CancellationToken.None);

        ScreenRecordingStopResult stop = await engine.StopAsync(
            start.Session!,
            CancellationToken.None);

        Assert.IsFalse(stop.Succeeded);
        Assert.AreEqual(writerFailure, stop.Failure);
        Assert.AreEqual(ScreenRecordingState.Failed, stop.Session?.State);
        Assert.AreEqual("OutputUnavailable", stop.Diagnostics["writerFailureReason"]);
        Assert.AreEqual("8", stop.Diagnostics["writerFinish:framesWritten"]);
    }

    [TestMethod]
    public async Task StopAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var engine = new ScreenRecordingPipelineEngine(
            new FakeFrameSource(),
            new FakeOutputWriter(),
            new FakeClock());
        var session = new ScreenRecordingSession(
            ScreenRecordingSessionId.New(),
            CaptureTargetKind.Display,
            DateTimeOffset.UtcNow,
            "recording.mp4",
            ScreenRecordingState.Stopping,
            new Dictionary<string, string>());

        ScreenRecordingStopResult result = await engine.StopAsync(session, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    private static ScreenRecordingStartRequest CreateRequest(
        bool includeMicrophoneAudio = false,
        bool includeSystemAudio = false)
    {
        return new ScreenRecordingStartRequest(
            CaptureTargetKind.Display,
            "recording.mp4",
            IncludeMicrophoneAudio: includeMicrophoneAudio,
            IncludeSystemAudio: includeSystemAudio);
    }

    private static ScreenRecordingOutput CreateOutput()
    {
        return new ScreenRecordingOutput(
            "recording.mp4",
            TimeSpan.FromSeconds(3),
            1920,
            1080,
            FileSizeBytes: 1024,
            ContainerFormat: "mp4",
            VideoCodec: "h264",
            AudioCodec: null);
    }

    private sealed class FakeAudioSource : IScreenRecordingAudioSource
    {
        private readonly ScreenRecordingAudioSourceOpenResult? _openResult;
        private readonly ScreenRecordingAudioSourceStartResult? _startResult;
        private readonly ScreenRecordingAudioSourceStopResult _stopResult;

        public FakeAudioSource(
            ScreenRecordingAudioSourceOpenResult? openResult = null,
            ScreenRecordingAudioSourceStartResult? startResult = null,
            ScreenRecordingAudioSourceStopResult? stopResult = null)
        {
            _openResult = openResult;
            _startResult = startResult;
            _stopResult = stopResult ?? ScreenRecordingAudioSourceStopResult.Success(
                new Dictionary<string, string> { ["samplesCaptured"] = "480" });
        }

        public int OpenCallCount { get; private set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<ScreenRecordingAudioSourceOpenResult> OpenAsync(
            ScreenRecordingAudioSourceOpenRequest request,
            CancellationToken cancellationToken)
        {
            OpenCallCount++;
            if (_openResult is not null)
            {
                return Task.FromResult(_openResult);
            }

            var session = new ScreenRecordingAudioSourceSession(
                request.SessionId,
                SampleRate: 48000,
                ChannelCount: 2,
                BitsPerSample: 16,
                Encoding: "Pcm16",
                SampleInterval: TimeSpan.FromMilliseconds(10),
                Metadata: new Dictionary<string, string> { ["adapter"] = "fake-audio" });

            return Task.FromResult(ScreenRecordingAudioSourceOpenResult.Success(session));
        }

        public Task<ScreenRecordingAudioSourceStartResult> StartAsync(
            ScreenRecordingAudioSourceStartRequest request,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            return Task.FromResult(_startResult ?? ScreenRecordingAudioSourceStartResult.Success());
        }

        public Task<ScreenRecordingAudioSourceStopResult> StopAsync(
            ScreenRecordingSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            return Task.FromResult(_stopResult);
        }
    }

    private sealed class FakeFrameSource : IScreenRecordingFrameSource
    {
        private readonly ScreenRecordingFrameSourceOpenResult? _openResult;
        private readonly ScreenRecordingFrameSourceStartResult? _startResult;
        private readonly ScreenRecordingFrameSourceStopResult _stopResult;

        public FakeFrameSource(
            ScreenRecordingFrameSourceOpenResult? openResult = null,
            ScreenRecordingFrameSourceStartResult? startResult = null,
            ScreenRecordingFrameSourceStopResult? stopResult = null)
        {
            _openResult = openResult;
            _startResult = startResult;
            _stopResult = stopResult ?? ScreenRecordingFrameSourceStopResult.Success();
        }

        public int OpenCallCount { get; private set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<ScreenRecordingFrameSourceOpenResult> OpenAsync(
            ScreenRecordingFrameSourceOpenRequest request,
            CancellationToken cancellationToken)
        {
            OpenCallCount++;
            if (_openResult is not null)
            {
                return Task.FromResult(_openResult);
            }

            var session = new ScreenRecordingFrameSourceSession(
                request.SessionId,
                1920,
                1080,
                "Bgra32",
                TimeSpan.FromMilliseconds(16.667),
                new Dictionary<string, string> { ["adapter"] = "wgc" });

            return Task.FromResult(ScreenRecordingFrameSourceOpenResult.Success(session));
        }

        public Task<ScreenRecordingFrameSourceStartResult> StartAsync(
            ScreenRecordingFrameSourceStartRequest request,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            return Task.FromResult(_startResult ?? ScreenRecordingFrameSourceStartResult.Success());
        }

        public Task<ScreenRecordingFrameSourceStopResult> StopAsync(
            ScreenRecordingSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            return Task.FromResult(_stopResult);
        }
    }

    private class FakeOutputWriter : IScreenRecordingOutputWriter
    {
        private readonly ScreenRecordingOutputWriterStartResult _startResult;
        private readonly ScreenRecordingOutputWriterFinishResult _finishResult;

        public FakeOutputWriter(
            ScreenRecordingOutputWriterStartResult? startResult = null,
            ScreenRecordingOutputWriterFinishResult? finishResult = null)
        {
            _startResult = startResult ?? ScreenRecordingOutputWriterStartResult.Success(
                new Dictionary<string, string> { ["container"] = "mp4" });
            _finishResult = finishResult ?? ScreenRecordingOutputWriterFinishResult.Success(CreateOutput());
        }

        public int StartCallCount { get; private set; }

        public int FinishCallCount { get; private set; }

        public ScreenRecordingOutputWriterStartRequest? LastStartRequest { get; private set; }

        public Task<ScreenRecordingOutputWriterStartResult> StartAsync(
            ScreenRecordingOutputWriterStartRequest request,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            LastStartRequest = request;
            return Task.FromResult(_startResult);
        }

        public Task<ScreenRecordingOutputWriterFinishResult> FinishAsync(
            ScreenRecordingSessionId sessionId,
            CancellationToken cancellationToken)
        {
            FinishCallCount++;
            return Task.FromResult(_finishResult);
        }

        public Task<ScreenRecordingFrameWriteResult> WriteFrameAsync(
            IScreenRecordingVideoFrame frame,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ScreenRecordingFrameWriteResult.Success());
        }
    }

    private sealed class FakeAudioOutputWriter : FakeOutputWriter, IScreenRecordingAudioSink
    {
        public Task<ScreenRecordingAudioWriteResult> WriteAudioAsync(
            IScreenRecordingAudioSample sample,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ScreenRecordingAudioWriteResult.Success());
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
