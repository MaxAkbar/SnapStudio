using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Capture;

public enum CaptureTargetKind
{
    Region,
    Window,
    Display,
    FullScreen
}

public enum ImagePixelFormat
{
    Unknown,
    Rgba32,
    Bgra32
}

public enum CaptureFailureReason
{
    Cancelled,
    NotImplemented,
    PermissionDenied,
    TargetUnavailable,
    Unsupported,
    Unknown
}

public enum CaptureTargetSelectionMode
{
    Automatic,
    Picker
}

public sealed record CaptureRequest(
    CaptureTargetKind TargetKind,
    bool IncludeCursor,
    TimeSpan Delay,
    string? TargetHint = null,
    RectD? Bounds = null,
    IReadOnlyDictionary<string, string>? TargetMetadata = null);

public sealed record StillCaptureCapability(
    bool IsSupported,
    IReadOnlyCollection<CaptureTargetKind> SupportedTargets,
    string? UnavailableReason)
{
    public static StillCaptureCapability Supported(
        IReadOnlyCollection<CaptureTargetKind> supportedTargets) => new(
            true,
            supportedTargets,
            null);

    public static StillCaptureCapability Unsupported(string unavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unavailableReason);

        return new StillCaptureCapability(
            false,
            [],
            unavailableReason);
    }
}

public sealed record CaptureTargetRequest(
    IReadOnlyCollection<CaptureTargetKind> AllowedTargets,
    bool AllowDelayedCapture,
    CaptureTargetSelectionMode SelectionMode = CaptureTargetSelectionMode.Automatic);

public sealed record CaptureTargetSelection(
    CaptureTargetKind TargetKind,
    string? TargetId,
    RectD? Bounds,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ScreenPreviewImage(
    string Path,
    int Width,
    int Height);

public sealed record ImageAsset(
    string Path,
    int Width,
    int Height,
    ImagePixelFormat PixelFormat);

public sealed record CaptureResult(
    CaptureId Id,
    DateTimeOffset CapturedAtUtc,
    ImageAsset SourceImage,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CaptureFailure(
    CaptureFailureReason Reason,
    string Message,
    Exception? Exception = null);

public sealed record CaptureOutcome(CaptureResult? Capture, CaptureFailure? Failure)
{
    public bool Succeeded => Capture is not null;

    public static CaptureOutcome Success(CaptureResult capture) => new(capture, null);

    public static CaptureOutcome Failed(CaptureFailure failure) => new(null, failure);
}

public sealed record CaptureWorkflowRequest(
    IReadOnlyCollection<CaptureTargetKind> AllowedTargets,
    bool IncludeCursor,
    TimeSpan Delay,
    CaptureTargetSelectionMode SelectionMode = CaptureTargetSelectionMode.Automatic)
{
    public static CaptureWorkflowRequest FullScreen(bool includeCursor = true) => new(
        [CaptureTargetKind.FullScreen],
        includeCursor,
        TimeSpan.Zero);
}

public sealed record CaptureWorkflowResult(
    bool Succeeded,
    DocumentId? DocumentId,
    CaptureFailure? Failure,
    CaptureWorkflowNotification? EditorNotification)
{
    public static CaptureWorkflowResult Success(
        DocumentId documentId,
        CaptureWorkflowNotification editorNotification) => new(
            true,
            documentId,
            null,
            editorNotification);

    public static CaptureWorkflowResult Failed(
        CaptureFailure failure,
        CaptureWorkflowNotification? editorNotification = null) => new(
            false,
            null,
            failure,
            editorNotification);
}

public sealed record CaptureWorkflowNotification(bool Succeeded, string? ErrorMessage);
