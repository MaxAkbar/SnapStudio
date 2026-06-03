using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.ScreenRecording;

public readonly record struct ScreenRecordingSessionId(Guid Value)
{
    public static ScreenRecordingSessionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public enum ScreenRecordingState
{
    Starting,
    Recording,
    Paused,
    Stopping,
    Stopped,
    Failed
}

public enum ScreenRecordingFailureReason
{
    Cancelled,
    InvalidRequest,
    NotEnabled,
    Unsupported,
    PermissionDenied,
    TargetUnavailable,
    EncoderUnavailable,
    AudioUnavailable,
    SessionNotFound,
    AlreadyRecording,
    OutputUnavailable,
    Unknown
}

public sealed record ScreenRecordingStartRequest(
    CaptureTargetKind TargetKind,
    string OutputPath,
    bool IncludeCursor = true,
    bool IncludeMicrophoneAudio = false,
    bool IncludeSystemAudio = false,
    TimeSpan? MaximumDuration = null,
    string? TargetHint = null,
    RectD? Bounds = null,
    IReadOnlyDictionary<string, string>? TargetMetadata = null);

public sealed record ScreenRecordingSession(
    ScreenRecordingSessionId Id,
    CaptureTargetKind TargetKind,
    DateTimeOffset StartedAtUtc,
    string OutputPath,
    ScreenRecordingState State,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ScreenRecordingOutput(
    string Path,
    TimeSpan Duration,
    int Width,
    int Height,
    long? FileSizeBytes,
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec);

public interface IScreenRecordingVideoFrame : IDisposable
{
    ScreenRecordingSessionId SessionId { get; }

    long SequenceNumber { get; }

    TimeSpan Timestamp { get; }

    int Width { get; }

    int Height { get; }

    string PixelFormat { get; }

    IReadOnlyDictionary<string, string> Metadata { get; }
}

public interface IScreenRecordingAudioSample : IDisposable
{
    ScreenRecordingSessionId SessionId { get; }

    long SequenceNumber { get; }

    TimeSpan Timestamp { get; }

    TimeSpan Duration { get; }

    int SampleRate { get; }

    int ChannelCount { get; }

    int BitsPerSample { get; }

    string Encoding { get; }

    IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed record ScreenRecordingFrameSourceOpenRequest(
    ScreenRecordingSessionId SessionId,
    ScreenRecordingStartRequest RecordingRequest);

public sealed record ScreenRecordingFrameSourceStartRequest(
    ScreenRecordingSessionId SessionId,
    IScreenRecordingFrameSink FrameSink);

public sealed record ScreenRecordingFrameSourceSession(
    ScreenRecordingSessionId SessionId,
    int Width,
    int Height,
    string PixelFormat,
    TimeSpan? FrameInterval,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ScreenRecordingAudioSourceOpenRequest(
    ScreenRecordingSessionId SessionId,
    ScreenRecordingStartRequest RecordingRequest);

public sealed record ScreenRecordingAudioSourceStartRequest(
    ScreenRecordingSessionId SessionId,
    IScreenRecordingAudioSink AudioSink);

public sealed record ScreenRecordingAudioSourceSession(
    ScreenRecordingSessionId SessionId,
    int SampleRate,
    int ChannelCount,
    int BitsPerSample,
    string Encoding,
    TimeSpan? SampleInterval,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ScreenRecordingOutputWriterStartRequest(
    ScreenRecordingSessionId SessionId,
    ScreenRecordingStartRequest RecordingRequest,
    ScreenRecordingFrameSourceSession SourceSession,
    ScreenRecordingAudioSourceSession? AudioSession = null);

public sealed record ScreenRecordingFailure(
    ScreenRecordingFailureReason Reason,
    string Message,
    Exception? Exception = null);

public sealed record ScreenRecordingStartResult(
    ScreenRecordingSession? Session,
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Session is not null && Failure is null;

    public static ScreenRecordingStartResult Success(ScreenRecordingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ScreenRecordingStartResult(session, null);
    }

    public static ScreenRecordingStartResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingStartResult(null, failure);
    }
}

public sealed record ScreenRecordingFrameSourceOpenResult(
    ScreenRecordingFrameSourceSession? Session,
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Session is not null && Failure is null;

    public static ScreenRecordingFrameSourceOpenResult Success(ScreenRecordingFrameSourceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ScreenRecordingFrameSourceOpenResult(session, null);
    }

    public static ScreenRecordingFrameSourceOpenResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingFrameSourceOpenResult(null, failure);
    }
}

public sealed record ScreenRecordingFrameSourceStartResult(
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingFrameSourceStartResult Success(
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        return new ScreenRecordingFrameSourceStartResult(
            null,
            diagnostics ?? new Dictionary<string, string>());
    }

    public static ScreenRecordingFrameSourceStartResult Failed(
        ScreenRecordingFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingFrameSourceStartResult(
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScreenRecordingFrameWriteResult(
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingFrameWriteResult Success()
    {
        return new ScreenRecordingFrameWriteResult(Failure: null);
    }

    public static ScreenRecordingFrameWriteResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingFrameWriteResult(failure);
    }
}

public sealed record ScreenRecordingAudioWriteResult(
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingAudioWriteResult Success()
    {
        return new ScreenRecordingAudioWriteResult(Failure: null);
    }

    public static ScreenRecordingAudioWriteResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingAudioWriteResult(failure);
    }
}

public sealed record ScreenRecordingFrameSourceStopResult(
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingFrameSourceStopResult Success(
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        return new ScreenRecordingFrameSourceStopResult(
            null,
            diagnostics ?? new Dictionary<string, string>());
    }

    public static ScreenRecordingFrameSourceStopResult Failed(
        ScreenRecordingFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingFrameSourceStopResult(
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScreenRecordingAudioSourceOpenResult(
    ScreenRecordingAudioSourceSession? Session,
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Session is not null && Failure is null;

    public static ScreenRecordingAudioSourceOpenResult Success(ScreenRecordingAudioSourceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ScreenRecordingAudioSourceOpenResult(session, null);
    }

    public static ScreenRecordingAudioSourceOpenResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingAudioSourceOpenResult(null, failure);
    }
}

public sealed record ScreenRecordingAudioSourceStartResult(
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingAudioSourceStartResult Success(
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        return new ScreenRecordingAudioSourceStartResult(
            null,
            diagnostics ?? new Dictionary<string, string>());
    }

    public static ScreenRecordingAudioSourceStartResult Failed(
        ScreenRecordingFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingAudioSourceStartResult(
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScreenRecordingAudioSourceStopResult(
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingAudioSourceStopResult Success(
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        return new ScreenRecordingAudioSourceStopResult(
            null,
            diagnostics ?? new Dictionary<string, string>());
    }

    public static ScreenRecordingAudioSourceStopResult Failed(
        ScreenRecordingFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingAudioSourceStopResult(
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScreenRecordingOutputWriterStartResult(
    IReadOnlyDictionary<string, string> Metadata,
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static ScreenRecordingOutputWriterStartResult Success(
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ScreenRecordingOutputWriterStartResult(
            metadata ?? new Dictionary<string, string>(),
            null);
    }

    public static ScreenRecordingOutputWriterStartResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingOutputWriterStartResult(new Dictionary<string, string>(), failure);
    }
}

public sealed record ScreenRecordingOutputWriterFinishResult(
    ScreenRecordingOutput? Output,
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Output is not null && Failure is null;

    public static ScreenRecordingOutputWriterFinishResult Success(
        ScreenRecordingOutput output,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        return new ScreenRecordingOutputWriterFinishResult(
            output,
            null,
            diagnostics ?? new Dictionary<string, string>());
    }

    public static ScreenRecordingOutputWriterFinishResult Failed(
        ScreenRecordingFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingOutputWriterFinishResult(
            null,
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScreenRecordingControlResult(
    ScreenRecordingSession? Session,
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Session is not null && Failure is null;

    public static ScreenRecordingControlResult Success(ScreenRecordingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ScreenRecordingControlResult(session, null);
    }

    public static ScreenRecordingControlResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingControlResult(null, failure);
    }
}

public sealed record ScreenRecordingStopResult(
    ScreenRecordingOutput? Output,
    ScreenRecordingSession? Session,
    ScreenRecordingFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Output is not null && Failure is null;

    public bool HasOutput => Output is not null;

    public static ScreenRecordingStopResult Success(
        ScreenRecordingOutput output,
        ScreenRecordingSession session,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(session);

        return new ScreenRecordingStopResult(output, session, null, diagnostics ?? new Dictionary<string, string>());
    }

    public static ScreenRecordingStopResult Failed(
        ScreenRecordingFailure failure,
        ScreenRecordingSession? session = null,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingStopResult(null, session, failure, diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScreenRecordingStatusResult(
    ScreenRecordingSession? Session,
    ScreenRecordingFailure? Failure)
{
    public bool Succeeded => Session is not null && Failure is null;

    public static ScreenRecordingStatusResult Success(ScreenRecordingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ScreenRecordingStatusResult(session, null);
    }

    public static ScreenRecordingStatusResult Failed(ScreenRecordingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScreenRecordingStatusResult(null, failure);
    }
}
