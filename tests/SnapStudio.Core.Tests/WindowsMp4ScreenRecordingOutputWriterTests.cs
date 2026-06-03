using SnapStudio.Core.Capture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsMp4ScreenRecordingOutputWriterTests
{
    [Fact]
    public async Task StartAsync_WhenOutputPathIsMissing_ReturnsInvalidRequest()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(outputPath: string.Empty),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    [Fact]
    public async Task StartAsync_WhenOutputPathIsNotMp4_ReturnsInvalidRequest()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(outputPath: Path.Combine(Path.GetTempPath(), "recording.png")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
    }

    [Fact]
    public async Task StartAsync_WhenRequestIsValid_ReturnsWriterMetadata()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        string outputPath = Path.Combine(workspace.Path, "nested", "recording.mp4");

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            CreateStartRequest(outputPath: outputPath),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("mp4", result.Metadata["container"]);
        Assert.Equal("h264", result.Metadata["videoCodec"]);
        Assert.Equal("pending", result.Metadata["encoderState"]);
        Assert.True(Directory.Exists(Path.GetDirectoryName(outputPath)));
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.Equal("pending", result.Metadata["audioState"]);
    }

    [Fact]
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

        Assert.True(write.Succeeded);
        Assert.True(finish.Succeeded);
        Assert.Equal(outputPath, finish.Output?.Path);
        Assert.Equal("1", finish.Diagnostics["framesReceived"]);
        Assert.Equal("33", finish.Diagnostics["firstFrameTimestampMilliseconds"]);
        Assert.Equal("33", finish.Diagnostics["lastFrameTimestampMilliseconds"]);
        Assert.Equal("1", finish.Diagnostics["firstSequenceNumber"]);
        Assert.Equal("1", finish.Diagnostics["lastSequenceNumber"]);
        Assert.Equal("0", finish.Diagnostics["sequenceGapCount"]);
        Assert.Equal("0.000", finish.Diagnostics["frameSpanMilliseconds"]);
        Assert.Equal("0", finish.Diagnostics["positiveFrameDeltaCount"]);
        Assert.Equal("1", finish.Diagnostics["encoder:framesReceived"]);
        Assert.NotNull(encoder.Request);
        Assert.Single(encoder.Request.Frames);
    }

    [Fact]
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

        Assert.True(frameWrite.Succeeded);
        Assert.True(audioWrite.Succeeded);
        Assert.True(finish.Succeeded);
        Assert.Equal("1", finish.Diagnostics["audioSamplesReceived"]);
        Assert.Equal("960", finish.Diagnostics["bufferedAudioBytes"]);
        Assert.Equal("10", finish.Diagnostics["firstAudioTimestampMilliseconds"]);
        Assert.Equal("10", finish.Diagnostics["lastAudioTimestampMilliseconds"]);
        Assert.NotNull(encoder.Request);
        Assert.NotNull(encoder.Request.AudioSession);
        Assert.Single(encoder.Request.AudioSamples!);
    }

    [Fact]
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

        Assert.True(finish.Succeeded);
        Assert.Equal("10", finish.Diagnostics["firstSequenceNumber"]);
        Assert.Equal("13", finish.Diagnostics["lastSequenceNumber"]);
        Assert.Equal("1", finish.Diagnostics["sequenceGapCount"]);
        Assert.Equal("150.000", finish.Diagnostics["frameSpanMilliseconds"]);
        Assert.Equal("2", finish.Diagnostics["positiveFrameDeltaCount"]);
        Assert.Equal("0", finish.Diagnostics["duplicateFrameTimestampCount"]);
        Assert.Equal("0", finish.Diagnostics["nonMonotonicFrameTimestampCount"]);
        Assert.Equal("75.000", finish.Diagnostics["averageFrameIntervalMilliseconds"]);
        Assert.Equal("50.000", finish.Diagnostics["minimumFrameIntervalMilliseconds"]);
        Assert.Equal("100.000", finish.Diagnostics["maximumFrameIntervalMilliseconds"]);
        Assert.Equal("13.333", finish.Diagnostics["estimatedFrameRate"]);
    }

    [Fact]
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

        Assert.False(finish.Succeeded);
        Assert.Equal(failure, finish.Failure);
        Assert.Equal("failed", finish.Diagnostics["encoder:encoderState"]);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [Fact]
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

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.OutputUnavailable, second.Failure?.Reason);
        Assert.False(finish.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.OutputUnavailable, finish.Failure?.Reason);
        Assert.Equal("1", finish.Diagnostics["bufferedAudioSamples"]);
        Assert.Equal("OutputUnavailable", finish.Diagnostics["bufferFailureReason"]);
        Assert.Null(encoder.Request);
    }

    [Fact]
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

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.OutputUnavailable, second.Failure?.Reason);
        Assert.False(finish.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.OutputUnavailable, finish.Failure?.Reason);
        Assert.Equal("1", finish.Diagnostics["bufferedFrames"]);
        Assert.Equal("OutputUnavailable", finish.Diagnostics["bufferFailureReason"]);
        Assert.Null(encoder.Request);
    }

    [Fact]
    public async Task WriteFrameAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();

        ScreenRecordingFrameWriteResult result = await writer.WriteFrameAsync(
            new FakeFrame(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [Fact]
    public async Task FinishAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());

        ScreenRecordingOutputWriterFinishResult result = await writer.FinishAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [Fact]
    public async Task WriteAudioAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var writer = new WindowsMp4ScreenRecordingOutputWriter(new FakeEncoder(), new FakeClock());
        ScreenRecordingSessionId sessionId = ScreenRecordingSessionId.New();

        ScreenRecordingAudioWriteResult result = await writer.WriteAudioAsync(
            new FakePcmAudioSample(sessionId, sequenceNumber: 1, TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.OutputUnavailable, result.Failure?.Reason);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
        Assert.Equal("0", result.Diagnostics["audioSamplesReceived"]);
        Assert.Equal("48000", result.Diagnostics["audioSampleRate"]);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
        Assert.Equal("1", result.Diagnostics["audioSamplesReceived"]);
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
