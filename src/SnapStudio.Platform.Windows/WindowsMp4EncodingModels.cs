using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Platform.Windows;

public interface IWindowsScreenRecordingPixelFrame : IScreenRecordingVideoFrame
{
    byte[] CopyPixelBytes();
}

public interface IWindowsScreenRecordingPcmAudioSample : IScreenRecordingAudioSample
{
    byte[] CopyAudioBytes();
}

public interface IWindowsMp4Encoder
{
    Task<WindowsMp4EncodingResult> EncodeAsync(
        WindowsMp4EncodingRequest request,
        CancellationToken cancellationToken);
}

public sealed record WindowsMp4ScreenRecordingOutputWriterOptions(
    int MaxBufferedFrames,
    long MaxBufferedBytes,
    int MaxBufferedAudioSamples = 18000,
    long MaxBufferedAudioBytes = 128L * 1024L * 1024L)
{
    public static WindowsMp4ScreenRecordingOutputWriterOptions Default { get; } = new(
        MaxBufferedFrames: 1800,
        MaxBufferedBytes: 512L * 1024L * 1024L,
        MaxBufferedAudioSamples: 18000,
        MaxBufferedAudioBytes: 128L * 1024L * 1024L);
}

public sealed record WindowsMp4BufferedFrame(
    long SequenceNumber,
    TimeSpan Timestamp,
    int Width,
    int Height,
    string PixelFormat,
    byte[] PixelBytes,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record WindowsMp4BufferedAudioSample(
    long SequenceNumber,
    TimeSpan Timestamp,
    TimeSpan Duration,
    int SampleRate,
    int ChannelCount,
    int BitsPerSample,
    string Encoding,
    byte[] AudioBytes,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record WindowsMp4EncodingRequest(
    string OutputPath,
    int Width,
    int Height,
    string PixelFormat,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset StoppedAtUtc,
    IReadOnlyList<WindowsMp4BufferedFrame> Frames,
    ScreenRecordingAudioSourceSession? AudioSession = null,
    IReadOnlyList<WindowsMp4BufferedAudioSample>? AudioSamples = null);

public sealed record WindowsMp4EncodingResult(
    ScreenRecordingOutput? Output,
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Output is not null && Failure is null;

    public static WindowsMp4EncodingResult Success(
        ScreenRecordingOutput output,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        return new WindowsMp4EncodingResult(
            output,
            null,
            diagnostics ?? new Dictionary<string, string>());
    }

    public static WindowsMp4EncodingResult Failed(
        ScreenRecordingFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new WindowsMp4EncodingResult(
            null,
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}
