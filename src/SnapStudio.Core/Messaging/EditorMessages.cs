using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Messaging;

public abstract record EditorMessage
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;

    public Guid MessageId { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public abstract string MessageType { get; }
}

public sealed record PingEditorMessage(string Sender) : EditorMessage
{
    public const string TypeName = "editor.ping.v1";

    public override string MessageType => TypeName;
}

public sealed record CaptureCompletedEditorMessage(
    CaptureId CaptureId,
    DocumentId DocumentId,
    string SourceImagePath) : EditorMessage
{
    public const string TypeName = "capture.completed.v1";

    public override string MessageType => TypeName;
}

public sealed record CaptureFailedEditorMessage(
    CaptureFailureReason Reason,
    string Message) : EditorMessage
{
    public const string TypeName = "capture.failed.v1";

    public override string MessageType => TypeName;
}

public sealed record EditorMessageSendResult(bool Succeeded, string? ErrorMessage)
{
    public static EditorMessageSendResult Success() => new(true, null);

    public static EditorMessageSendResult Failed(string errorMessage) => new(false, errorMessage);
}
