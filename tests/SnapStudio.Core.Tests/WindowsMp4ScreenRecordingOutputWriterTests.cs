using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsMp4ScreenRecordingOutputWriterTests
{
    [TestMethod]
    public async Task StartAsync_WhenOutputPathIsMissing_ReturnsInvalidRequest()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(outputPath: string.Empty),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StartAsync_WhenOutputPathIsNotMp4_ReturnsInvalidRequest()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(outputPath: Path.Combine(Path.GetTempPath(), "recording.png")),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StartAsync_WhenRequestIsValid_ReturnsWriterMetadata()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        string outputPath = Path.Combine(workspace.Path, "nested", "recording.mp4");

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(outputPath: outputPath),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("mp4", result.Metadata["container"]);
        Assert.AreEqual("h264", result.Metadata["videoCodec"]);
        Assert.AreEqual("pending", result.Metadata["encoderState"]);
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(outputPath)));
    }

    [TestMethod]
    public async Task StartAsync_WhenAudioSessionIsProvided_ReturnsWriterMetadata()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(
                sessionId,
                Path.Combine(workspace.Path, "recording.mp4"),
                CreateAudioSession(sessionId)),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("pending", result.Metadata["audioState"]);
    }

    [TestMethod]
    public async Task FinishAsync_WhenEncoderSucceeds_ReturnsOutputAndDiagnostics()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var encoder = new FakeEncoder();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(encoder, new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        string outputPath = Path.Combine(workspace.Path, "recording.mp4");
        await writer.StartAsync(
            CreateStartRequest(sessionId, outputPath),
            CancellationToken.None);

        ScreenRecordingFrameWriteResult write = await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 1, TimeSpan.FromMilliseconds(33)),
            CancellationToken.None);
        ScreenRecordingOutputWriterFinishResult finish = await writer.FinishAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsTrue(write.Succeeded);
        Assert.IsTrue(finish.Succeeded);
        Assert.AreEqual(outputPath, finish.Output?.Path);
        Assert.AreEqual("1", finish.Diagnostics["framesReceived"]);
        Assert.AreEqual("33", finish.Diagnostics["firstFrameTimestampMilliseconds"]);
        Assert.AreEqual("33", finish.Diagnostics["lastFrameTimestampMilliseconds"]);
        Assert.AreEqual("1", finish.Diagnostics["firstSequenceNumber"]);
        Assert.AreEqual("1", finish.Diagnostics["lastSequenceNumber"]);
        Assert.AreEqual("0", finish.Diagnostics["sequenceGapCount"]);
        Assert.AreEqual("0.000", finish.Diagnostics["frameSpanMilliseconds"]);
        Assert.AreEqual("0", finish.Diagnostics["positiveFrameDeltaCount"]);
        Assert.AreEqual("1", finish.Diagnostics["encoder:framesReceived"]);
        Assert.IsNotNull(encoder.Request);
        Assert.ContainsSingle(encoder.Request.Frames);
    }

    [TestMethod]
    public async Task FinishAsync_WhenAudioIsBuffered_DelegatesAudioSamplesAndDiagnostics()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var encoder = new FakeEncoder();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(encoder, new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(
                sessionId,
                Path.Combine(workspace.Path, "recording.mp4"),
                CreateAudioSession(sessionId)),
            CancellationToken.None);

        ScreenRecordingFrameWriteResult frameWrite = await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);
        ScreenRecordingAudioWriteResult audioWrite = await writer.WriteAudioAsync(
            new FakePcmAudioSample(
                sessionId,
                sequenceNumber: 1,
                TimeSpan.FromMilliseconds(10)),
            CancellationToken.None);
        ScreenRecordingOutputWriterFinishResult finish = await writer.FinishAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsTrue(frameWrite.Succeeded);
        Assert.IsTrue(audioWrite.Succeeded);
        Assert.IsTrue(finish.Succeeded);
        Assert.AreEqual("1", finish.Diagnostics["audioSamplesReceived"]);
        Assert.AreEqual("960", finish.Diagnostics["bufferedAudioBytes"]);
        Assert.AreEqual("10", finish.Diagnostics["firstAudioTimestampMilliseconds"]);
        Assert.AreEqual("10", finish.Diagnostics["lastAudioTimestampMilliseconds"]);
        Assert.IsNotNull(encoder.Request);
        Assert.IsNotNull(encoder.Request.AudioSession);
        Assert.ContainsSingle(encoder.Request.AudioSamples!);
    }

    [TestMethod]
    public async Task FinishAsync_WhenFramesHaveTimingGaps_ReturnsSyncDiagnostics()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(sessionId, Path.Combine(workspace.Path, "recording.mp4")),
            CancellationToken.None);

        await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 10, TimeSpan.Zero),
            CancellationToken.None);
        await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 12, TimeSpan.FromMilliseconds(50)),
            CancellationToken.None);
        await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 13, TimeSpan.FromMilliseconds(150)),
            CancellationToken.None);

        ScreenRecordingOutputWriterFinishResult finish = await writer.FinishAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsTrue(finish.Succeeded);
        Assert.AreEqual("10", finish.Diagnostics["firstSequenceNumber"]);
        Assert.AreEqual("13", finish.Diagnostics["lastSequenceNumber"]);
        Assert.AreEqual("1", finish.Diagnostics["sequenceGapCount"]);
        Assert.AreEqual("150.000", finish.Diagnostics["frameSpanMilliseconds"]);
        Assert.AreEqual("2", finish.Diagnostics["positiveFrameDeltaCount"]);
        Assert.AreEqual("0", finish.Diagnostics["duplicateFrameTimestampCount"]);
        Assert.AreEqual("0", finish.Diagnostics["nonMonotonicFrameTimestampCount"]);
        Assert.AreEqual("75.000", finish.Diagnostics["averageFrameIntervalMilliseconds"]);
        Assert.AreEqual("50.000", finish.Diagnostics["minimumFrameIntervalMilliseconds"]);
        Assert.AreEqual("100.000", finish.Diagnostics["maximumFrameIntervalMilliseconds"]);
        Assert.AreEqual("13.333", finish.Diagnostics["estimatedFrameRate"]);
    }

    [TestMethod]
    public async Task FinishAsync_WhenEncoderFails_ReturnsTypedFailureWithDiagnostics()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var failure = new ScreenRecordingFailure(
            ScreenRecordingFailureReason.EncoderUnavailable,
            "No encoder.");
        var writer = new WindowsMp4ScreenRecordingOutputWriter(
            new FakeEncoder(WindowsMp4EncodingResult.Failed(
                failure,
                new Dictionary<string, string> { ["encoderState"] = "failed" })),
            new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(sessionId, Path.Combine(workspace.Path, "recording.mp4")),
            CancellationToken.None);

        ScreenRecordingOutputWriterFinishResult finish = await writer.FinishAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsFalse(finish.Succeeded);
        Assert.AreEqual(failure, finish.Failure);
        Assert.AreEqual("failed", finish.Diagnostics["encoder:encoderState"]);
    }

    [TestMethod]
    public async Task WriteFrameAsync_WhenFrameIsNotPixelFrame_ReturnsEncoderUnavailable()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(sessionId, Path.Combine(workspace.Path, "recording.mp4")),
            CancellationToken.None);

        ScreenRecordingFrameWriteResult result = await writer.WriteFrameAsync(
            new FakeFrame(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WriteAudioAsync_WhenSessionHasNoAudio_ReturnsAudioUnavailable()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(sessionId, Path.Combine(workspace.Path, "recording.mp4")),
            CancellationToken.None);

        ScreenRecordingAudioWriteResult result = await writer.WriteAudioAsync(
            new FakePcmAudioSample(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WriteAudioAsync_WhenSampleIsNotPcm_ReturnsEncoderUnavailable()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(
                sessionId,
                Path.Combine(workspace.Path, "recording.mp4"),
                CreateAudioSession(sessionId)),
            CancellationToken.None);

        ScreenRecordingAudioWriteResult result = await writer.WriteAudioAsync(
            new FakeAudioSample(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WriteAudioAsync_WhenAudioLimitIsReached_FailsFinishWithoutEncoding()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var encoder = new FakeEncoder();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(
            encoder,
            new FakeClock(),
            new WindowsMp4ScreenRecordingOutputWriterOptions(
                MaxBufferedFrames: 10,
                MaxBufferedBytes: long.MaxValue,
                MaxBufferedAudioSamples: 1,
                MaxBufferedAudioBytes: long.MaxValue));
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(
                sessionId,
                Path.Combine(workspace.Path, "recording.mp4"),
                CreateAudioSession(sessionId)),
            CancellationToken.None);

        ScreenRecordingAudioWriteResult first = await writer.WriteAudioAsync(
            new FakePcmAudioSample(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);
        ScreenRecordingAudioWriteResult second = await writer.WriteAudioAsync(
            new FakePcmAudioSample(sessionId, sequenceNumber: 2, TimeSpan.FromMilliseconds(10)),
            CancellationToken.None);
        ScreenRecordingOutputWriterFinishResult finish = await writer.FinishAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.OutputUnavailable, second.Failure?.Reason);
        Assert.IsFalse(finish.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.OutputUnavailable, finish.Failure?.Reason);
        Assert.AreEqual("1", finish.Diagnostics["bufferedAudioSamples"]);
        Assert.AreEqual("OutputUnavailable", finish.Diagnostics["bufferFailureReason"]);
        Assert.IsNull(encoder.Request);
    }

    [TestMethod]
    public async Task WriteFrameAsync_WhenFrameLimitIsReached_FailsFinishWithoutEncoding()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var encoder = new FakeEncoder();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(
            encoder,
            new FakeClock(),
            new WindowsMp4ScreenRecordingOutputWriterOptions(
                MaxBufferedFrames: 1,
                MaxBufferedBytes: long.MaxValue));
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        await writer.StartAsync(
            CreateStartRequest(sessionId, Path.Combine(workspace.Path, "recording.mp4")),
            CancellationToken.None);

        ScreenRecordingFrameWriteResult first = await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);
        ScreenRecordingFrameWriteResult second = await writer.WriteFrameAsync(
            new FakePixelFrame(sessionId, sequenceNumber: 2, TimeSpan.FromMilliseconds(33)),
            CancellationToken.None);
        ScreenRecordingOutputWriterFinishResult finish = await writer.FinishAsync(
            sessionId,
            CancellationToken.None);

        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.OutputUnavailable, second.Failure?.Reason);
        Assert.IsFalse(finish.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.OutputUnavailable, finish.Failure?.Reason);
        Assert.AreEqual("1", finish.Diagnostics["bufferedFrames"]);
        Assert.AreEqual("OutputUnavailable", finish.Diagnostics["bufferFailureReason"]);
        Assert.IsNull(encoder.Request);
    }

    [TestMethod]
    public async Task WriteFrameAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();

        ScreenRecordingFrameWriteResult result = await writer.WriteFrameAsync(
            new FakeFrame(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task FinishAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());

        ScreenRecordingOutputWriterFinishResult result = await writer.FinishAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WriteAudioAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();

        ScreenRecordingAudioWriteResult result = await writer.WriteAudioAsync(
            new FakePcmAudioSample(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WindowsMediaTranscoderMp4Encoder_WhenNoFrames_ReturnsOutputUnavailable()
    {
        var encoder = new WindowsMediaTranscoderMp4Encoder();

        WindowsMp4EncodingResult result = await encoder.EncodeAsync(
            new WindowsMp4EncodingRequest(
                Path.Combine(Path.GetTempPath(), "recording.mp4"),
                1280,
                720,
                "Bgra32",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                []),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.OutputUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WindowsMediaTranscoderMp4Encoder_WhenDimensionsAreOdd_ReturnsEncoderUnavailable()
    {
        var encoder = new WindowsMediaTranscoderMp4Encoder();

        WindowsMp4EncodingResult result = await encoder.EncodeAsync(
            new WindowsMp4EncodingRequest(
                Path.Combine(Path.GetTempPath(), "recording.mp4"),
                1279,
                719,
                "Bgra32",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [
                    new WindowsMp4BufferedFrame(
                        1,
                        TimeSpan.Zero,
                        1279,
                        719,
                        "Bgra32",
                        new byte[1279 * 719 * 4],
                        new Dictionary<string, string>())
                ]),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task WindowsMediaTranscoderMp4Encoder_WhenAudioSessionHasNoSamples_ReturnsAudioUnavailable()
    {
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        var encoder = new WindowsMediaTranscoderMp4Encoder();

        WindowsMp4EncodingResult result = await encoder.EncodeAsync(
            new WindowsMp4EncodingRequest(
                Path.Combine(Path.GetTempPath(), "recording.mp4"),
                1280,
                720,
                "Bgra32",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [
                    new WindowsMp4BufferedFrame(
                        1,
                        TimeSpan.Zero,
                        1280,
                        720,
                        "Bgra32",
                        new byte[1280 * 720 * 4],
                        new Dictionary<string, string>())
                ],
                CreateAudioSession(sessionId),
                []),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
        Assert.AreEqual("0", result.Diagnostics["audioSamplesReceived"]);
        Assert.AreEqual("48000", result.Diagnostics["audioSampleRate"]);
    }

    [TestMethod]
    public async Task WindowsMediaTranscoderMp4Encoder_WhenAudioSampleFormatMismatchesSession_ReturnsInvalidRequest()
    {
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();
        var encoder = new WindowsMediaTranscoderMp4Encoder();

        WindowsMp4EncodingResult result = await encoder.EncodeAsync(
            new WindowsMp4EncodingRequest(
                Path.Combine(Path.GetTempPath(), "recording.mp4"),
                1280,
                720,
                "Bgra32",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [
                    new WindowsMp4BufferedFrame(
                        1,
                        TimeSpan.Zero,
                        1280,
                        720,
                        "Bgra32",
                        new byte[1280 * 720 * 4],
                        new Dictionary<string, string>())
                ],
                CreateAudioSession(sessionId),
                [
                    CreateBufferedAudioSample() with { ChannelCount = 1 }
                ]),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
        Assert.AreEqual("1", result.Diagnostics["audioSamplesReceived"]);
    }

    private static ScreenRecordingOutputWriterStartRequest CreateStartRequest(
        ScreenRecordingSessionId? sessionId = null,
        string? outputPath = null,
        ScreenRecordingAudioSourceSession? audioSession = null)
    {
        ScreenRecordingSessionId resolvedSessionId = sessionId ?? ScreenRecordingSessionId.New();
        return new ScreenRecordingOutputWriterStartRequest(
            resolvedSessionId,
            new ScreenRecordingStartRequest(
                CaptureTargetKind.Display,
                outputPath ?? Path.Combine(Path.GetTempPath(), "recording.mp4")),
            new ScreenRecordingFrameSourceSession(
                resolvedSessionId,
                1280,
                720,
                "Bgra32",
                TimeSpan.FromMilliseconds(16.667),
                new Dictionary<string, string>()),
            audioSession);
    }

    private static ScreenRecordingAudioSourceSession CreateAudioSession(ScreenRecordingSessionId sessionId)
    {
        return new ScreenRecordingAudioSourceSession(
            sessionId,
            SampleRate: 48000,
            ChannelCount: 2,
            BitsPerSample: 16,
            Encoding: "Pcm16",
            SampleInterval: TimeSpan.FromMilliseconds(10),
            Metadata: new Dictionary<string, string>());
    }

    private static WindowsMp4BufferedAudioSample CreateBufferedAudioSample()
    {
        return new WindowsMp4BufferedAudioSample(
            SequenceNumber: 1,
            Timestamp: TimeSpan.Zero,
            Duration: TimeSpan.FromMilliseconds(10),
            SampleRate: 48000,
            ChannelCount: 2,
            BitsPerSample: 16,
            Encoding: "Pcm16",
            AudioBytes: new byte[1920],
            Metadata: new Dictionary<string, string>());
    }

    private class FakeFrame(
        ScreenRecordingSessionId sessionId,
        long sequenceNumber,
        TimeSpan timestamp) : IScreenRecordingVideoFrame
    {
        public ScreenRecordingSessionId SessionId { get; } = sessionId;

        public long SequenceNumber { get; } = sequenceNumber;

        public TimeSpan Timestamp { get; } = timestamp;

        public int Width => 1280;

        public int Height => 720;

        public string PixelFormat => "Bgra32";

        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public void Dispose()
        {
        }
    }

    private sealed class FakePixelFrame(
        ScreenRecordingSessionId sessionId,
        long sequenceNumber,
        TimeSpan timestamp) : FakeFrame(sessionId, sequenceNumber, timestamp), IWindowsScreenRecordingPixelFrame
    {
        public byte[] CopyPixelBytes()
        {
            return new byte[Width * Height * 4];
        }
    }

    private class FakeAudioSample(
        ScreenRecordingSessionId sessionId,
        long sequenceNumber,
        TimeSpan timestamp) : IScreenRecordingAudioSample
    {
        public ScreenRecordingSessionId SessionId { get; } = sessionId;

        public long SequenceNumber { get; } = sequenceNumber;

        public TimeSpan Timestamp { get; } = timestamp;

        public TimeSpan Duration => TimeSpan.FromMilliseconds(10);

        public int SampleRate => 48000;

        public int ChannelCount => 2;

        public int BitsPerSample => 16;

        public string Encoding => "Pcm16";

        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public void Dispose()
        {
        }
    }

    private sealed class FakePcmAudioSample(
        ScreenRecordingSessionId sessionId,
        long sequenceNumber,
        TimeSpan timestamp) : FakeAudioSample(sessionId, sequenceNumber, timestamp), IWindowsScreenRecordingPcmAudioSample
    {
        public byte[] CopyAudioBytes()
        {
            return new byte[960];
        }
    }

    private sealed class FakeEncoder(
        WindowsMp4EncodingResult? result = null) : IWindowsMp4Encoder
    {
        public WindowsMp4EncodingRequest? Request { get; private set; }

        public Task<WindowsMp4EncodingResult> EncodeAsync(
            WindowsMp4EncodingRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            WindowsMp4EncodingResult resolvedResult = result ?? WindowsMp4EncodingResult.Success(
                new ScreenRecordingOutput(
                    request.OutputPath,
                    TimeSpan.FromSeconds(1),
                    request.Width,
                    request.Height,
                    FileSizeBytes: 4096,
                    ContainerFormat: "mp4",
                    VideoCodec: "h264",
                    AudioCodec: null),
                new Dictionary<string, string>
                {
                    ["framesReceived"] = request.Frames.Count.ToString()
                });

            return Task.FromResult(resolvedResult);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
