using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.ScrollingCapture;

public enum ScrollTargetKind
{
    Unknown,
    Window,
    Control,
    Browser,
    DocumentViewer
}

public enum ScrollingCaptureFailureReason
{
    Cancelled,
    InvalidRequest,
    NotEnabled,
    Unsupported,
    TargetUnavailable,
    FrameCaptureFailed,
    ScrollFailed,
    StitchFailed,
    Unknown
}

public enum ScrollInputDirection
{
    Down,
    Up,
    Right,
    Left
}

public enum ScrollInputFailureReason
{
    Unsupported,
    TargetUnavailable,
    CannotScroll,
    Unknown
}

public sealed record ScrollTargetCandidate(
    string Id,
    string DisplayName,
    ScrollTargetKind Kind,
    RectD Bounds,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ScrollTargetDetectionRequest(
    RectD? SearchBounds = null,
    string? TargetHint = null);

public sealed record ScrollingCaptureRequest(
    ScrollTargetCandidate Target,
    string OutputDirectory,
    int MaximumFrames = 80,
    double MinimumOverlapRatio = 0.15);

public sealed record ScrollingCaptureFrame(
    int Index,
    ImageAsset Image,
    RectD TargetBounds,
    double ScrollOffset,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ScrollingFrameCaptureRequest(
    ScrollTargetCandidate Target,
    int FrameIndex,
    string OutputDirectory);

public sealed record ScrollingFrameCaptureResult(
    ScrollingCaptureFrame? Frame,
    ScrollingCaptureFailure? Failure)
{
    public bool Succeeded => Frame is not null && Failure is null;

    public static ScrollingFrameCaptureResult Success(ScrollingCaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return new ScrollingFrameCaptureResult(frame, null);
    }

    public static ScrollingFrameCaptureResult Failed(ScrollingCaptureFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScrollingFrameCaptureResult(null, failure);
    }
}

public sealed record ScrollingStitchRequest(
    IReadOnlyList<ScrollingCaptureFrame> Frames,
    string OutputDirectory,
    IReadOnlyDictionary<string, string> Options);

public sealed record ScrollingCaptureFailure(
    ScrollingCaptureFailureReason Reason,
    string Message,
    Exception? Exception = null);

public sealed record ScrollingCaptureDiagnosticsBundleRequest(
    ScrollingCaptureRequest CaptureRequest,
    ScrollingCaptureResult CaptureResult);

public sealed record ScrollingCaptureDiagnosticsBundleResult(
    string? BundlePath,
    string? ErrorMessage)
{
    public bool Succeeded => !string.IsNullOrWhiteSpace(BundlePath)
        && string.IsNullOrWhiteSpace(ErrorMessage);

    public static ScrollingCaptureDiagnosticsBundleResult Success(string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);

        return new ScrollingCaptureDiagnosticsBundleResult(bundlePath, null);
    }

    public static ScrollingCaptureDiagnosticsBundleResult Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new ScrollingCaptureDiagnosticsBundleResult(null, errorMessage);
    }
}

public sealed record ScrollInputRequest(
    ScrollTargetCandidate Target,
    ScrollInputDirection Direction);

public sealed record ScrollInputFailure(
    ScrollInputFailureReason Reason,
    string Message,
    Exception? Exception = null);

public sealed record ScrollInputResult(
    bool Succeeded,
    bool ReachedEnd,
    double? VerticalScrollPercent,
    double? HorizontalScrollPercent,
    ScrollInputFailure? Failure)
{
    public static ScrollInputResult Success(
        bool reachedEnd,
        double? verticalScrollPercent,
        double? horizontalScrollPercent) => new(
        true,
        reachedEnd,
        verticalScrollPercent,
        horizontalScrollPercent,
        null);

    public static ScrollInputResult Failed(
        ScrollInputFailure failure,
        double? verticalScrollPercent = null,
        double? horizontalScrollPercent = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScrollInputResult(
            false,
            false,
            verticalScrollPercent,
            horizontalScrollPercent,
            failure);
    }
}

public sealed record ScrollingStitchResult(
    ImageAsset? Image,
    bool IsPartial,
    ScrollingCaptureFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Image is not null && Failure is null;

    public bool HasOutput => Image is not null;

    public static ScrollingStitchResult Success(
        ImageAsset image,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        return new ScrollingStitchResult(image, false, null, diagnostics ?? new Dictionary<string, string>());
    }

    public static ScrollingStitchResult Partial(
        ImageAsset image,
        ScrollingCaptureFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(failure);

        return new ScrollingStitchResult(image, true, failure, diagnostics ?? new Dictionary<string, string>());
    }

    public static ScrollingStitchResult Failed(
        ScrollingCaptureFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScrollingStitchResult(null, false, failure, diagnostics ?? new Dictionary<string, string>());
    }
}

public sealed record ScrollingCaptureResult(
    ImageAsset? Image,
    IReadOnlyList<ScrollingCaptureFrame> Frames,
    bool IsPartial,
    ScrollingCaptureFailure? Failure,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public bool Succeeded => Image is not null && Failure is null;

    public bool HasOutput => Image is not null;

    public static ScrollingCaptureResult Success(
        ImageAsset image,
        IReadOnlyList<ScrollingCaptureFrame> frames,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(frames);

        return new ScrollingCaptureResult(image, frames, false, null, diagnostics ?? new Dictionary<string, string>());
    }

    public static ScrollingCaptureResult Partial(
        ImageAsset image,
        IReadOnlyList<ScrollingCaptureFrame> frames,
        ScrollingCaptureFailure failure,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(failure);

        return new ScrollingCaptureResult(image, frames, true, failure, diagnostics ?? new Dictionary<string, string>());
    }

    public static ScrollingCaptureResult Failed(
        ScrollingCaptureFailure failure,
        IReadOnlyList<ScrollingCaptureFrame>? frames = null,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ScrollingCaptureResult(
            null,
            frames ?? [],
            false,
            failure,
            diagnostics ?? new Dictionary<string, string>());
    }
}
