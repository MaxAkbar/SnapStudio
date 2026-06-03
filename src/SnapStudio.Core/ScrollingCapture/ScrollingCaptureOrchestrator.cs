using System.Globalization;

namespace SnapStudio.Core.ScrollingCapture;

public sealed class ScrollingCaptureOrchestrator : IScrollingCaptureService
{
    private const string DiagnosticsBundleErrorKey = "diagnosticsBundleError";
    private const string DiagnosticsBundlePathKey = "diagnosticsBundlePath";

    private readonly IScrollingCaptureDiagnosticsBundleWriter? _diagnosticsBundleWriter;
    private readonly IScrollingFrameCaptureService _frameCaptureService;
    private readonly IScrollInputController _scrollInputController;
    private readonly IScrollingStitcher _stitcher;

    public ScrollingCaptureOrchestrator(
        IScrollingFrameCaptureService frameCaptureService,
        IScrollInputController scrollInputController,
        IScrollingStitcher stitcher,
        IScrollingCaptureDiagnosticsBundleWriter? diagnosticsBundleWriter = null)
    {
        ArgumentNullException.ThrowIfNull(frameCaptureService);
        ArgumentNullException.ThrowIfNull(scrollInputController);
        ArgumentNullException.ThrowIfNull(stitcher);

        _frameCaptureService = frameCaptureService;
        _scrollInputController = scrollInputController;
        _stitcher = stitcher;
        _diagnosticsBundleWriter = diagnosticsBundleWriter;
    }

    public async Task<ScrollingCaptureResult> CaptureAsync(
        ScrollingCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ScrollingCaptureFailure? validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
        {
            return await AttachDiagnosticsBundleAsync(
                    request,
                    ScrollingCaptureResult.Failed(validationFailure),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var frames = new List<ScrollingCaptureFrame>();
        ScrollingFrameCaptureResult firstFrame = await CaptureFrameAsync(
                request,
                frameIndex: 0,
                cancellationToken)
            .ConfigureAwait(false);

        if (!firstFrame.Succeeded || firstFrame.Frame is not ScrollingCaptureFrame frame)
        {
            return await AttachDiagnosticsBundleAsync(
                    request,
                    ScrollingCaptureResult.Failed(
                        firstFrame.Failure ?? new ScrollingCaptureFailure(
                            ScrollingCaptureFailureReason.FrameCaptureFailed,
                            "The first scrolling capture frame could not be captured.")),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        frames.Add(frame);

        while (frames.Count < request.MaximumFrames)
        {
            ScrollInputResult scrollResult = await _scrollInputController
                .ScrollAsync(
                    new ScrollInputRequest(request.Target, ScrollInputDirection.Down),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!scrollResult.Succeeded)
            {
                ScrollingCaptureResult result = await CreatePartialOrFailedAsync(
                        request,
                        frames,
                        CreateScrollFailure(scrollResult),
                        cancellationToken)
                    .ConfigureAwait(false);

                return await AttachDiagnosticsBundleAsync(request, result, cancellationToken)
                    .ConfigureAwait(false);
            }

            ScrollingFrameCaptureResult nextFrame = await CaptureFrameAsync(
                    request,
                    frames.Count,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!nextFrame.Succeeded || nextFrame.Frame is not ScrollingCaptureFrame next)
            {
                ScrollingCaptureResult result = await CreatePartialOrFailedAsync(
                        request,
                        frames,
                        nextFrame.Failure ?? new ScrollingCaptureFailure(
                            ScrollingCaptureFailureReason.FrameCaptureFailed,
                            "A scrolling capture frame could not be captured."),
                        cancellationToken)
                    .ConfigureAwait(false);

                return await AttachDiagnosticsBundleAsync(request, result, cancellationToken)
                    .ConfigureAwait(false);
            }

            frames.Add(next);

            if (scrollResult.ReachedEnd)
            {
                break;
            }
        }

        ScrollingCaptureResult finalResult = await StitchFinalAsync(request, frames, cancellationToken)
            .ConfigureAwait(false);

        return await AttachDiagnosticsBundleAsync(request, finalResult, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScrollingCaptureResult> AttachDiagnosticsBundleAsync(
        ScrollingCaptureRequest request,
        ScrollingCaptureResult result,
        CancellationToken cancellationToken)
    {
        if (_diagnosticsBundleWriter is null)
        {
            return result;
        }

        try
        {
            ScrollingCaptureDiagnosticsBundleResult bundleResult = await _diagnosticsBundleWriter
                .WriteAsync(
                    new ScrollingCaptureDiagnosticsBundleRequest(request, result),
                    cancellationToken)
                .ConfigureAwait(false);

            if (bundleResult.Succeeded && bundleResult.BundlePath is not null)
            {
                return WithDiagnostic(result, DiagnosticsBundlePathKey, bundleResult.BundlePath);
            }

            return WithDiagnostic(
                result,
                DiagnosticsBundleErrorKey,
                bundleResult.ErrorMessage ?? "Scrolling capture diagnostics bundle could not be written.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return WithDiagnostic(
                result,
                DiagnosticsBundleErrorKey,
                $"Scrolling capture diagnostics bundle could not be written: {exception.Message}");
        }
    }

    private async Task<ScrollingFrameCaptureResult> CaptureFrameAsync(
        ScrollingCaptureRequest request,
        int frameIndex,
        CancellationToken cancellationToken)
    {
        return await _frameCaptureService
            .CaptureFrameAsync(
                new ScrollingFrameCaptureRequest(
                    request.Target,
                    frameIndex,
                    request.OutputDirectory),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScrollingCaptureResult> StitchFinalAsync(
        ScrollingCaptureRequest request,
        IReadOnlyList<ScrollingCaptureFrame> frames,
        CancellationToken cancellationToken)
    {
        ScrollingStitchResult stitchResult = await _stitcher
            .StitchAsync(CreateStitchRequest(request, frames), cancellationToken)
            .ConfigureAwait(false);

        if (stitchResult.Succeeded && stitchResult.Image is not null)
        {
            return ScrollingCaptureResult.Success(
                stitchResult.Image,
                frames,
                stitchResult.Diagnostics);
        }

        if (stitchResult.HasOutput && stitchResult.Image is not null)
        {
            return ScrollingCaptureResult.Partial(
                stitchResult.Image,
                frames,
                stitchResult.Failure ?? new ScrollingCaptureFailure(
                    ScrollingCaptureFailureReason.StitchFailed,
                    "Scrolling capture produced partial stitched output."),
                stitchResult.Diagnostics);
        }

        return ScrollingCaptureResult.Failed(
            stitchResult.Failure ?? new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.StitchFailed,
                "Scrolling capture frames could not be stitched."),
            frames,
            stitchResult.Diagnostics);
    }

    private async Task<ScrollingCaptureResult> CreatePartialOrFailedAsync(
        ScrollingCaptureRequest request,
        IReadOnlyList<ScrollingCaptureFrame> frames,
        ScrollingCaptureFailure captureFailure,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            return ScrollingCaptureResult.Failed(captureFailure);
        }

        ScrollingStitchResult stitchResult = await _stitcher
            .StitchAsync(CreateStitchRequest(request, frames), cancellationToken)
            .ConfigureAwait(false);

        if (stitchResult.HasOutput && stitchResult.Image is not null)
        {
            return ScrollingCaptureResult.Partial(
                stitchResult.Image,
                frames,
                captureFailure,
                MergeDiagnostics(stitchResult.Diagnostics, captureFailure));
        }

        return ScrollingCaptureResult.Failed(
            captureFailure,
            frames,
            MergeDiagnostics(stitchResult.Diagnostics, captureFailure));
    }

    private static ScrollingStitchRequest CreateStitchRequest(
        ScrollingCaptureRequest request,
        IReadOnlyList<ScrollingCaptureFrame> frames)
    {
        return new ScrollingStitchRequest(
            frames,
            request.OutputDirectory,
            new Dictionary<string, string>
            {
                ["maximumFrames"] = request.MaximumFrames.ToString(CultureInfo.InvariantCulture),
                ["minimumOverlapRatio"] = request.MinimumOverlapRatio.ToString(CultureInfo.InvariantCulture)
            });
    }

    private static IReadOnlyDictionary<string, string> MergeDiagnostics(
        IReadOnlyDictionary<string, string> diagnostics,
        ScrollingCaptureFailure failure)
    {
        var merged = new Dictionary<string, string>(diagnostics)
        {
            ["failureReason"] = failure.Reason.ToString(),
            ["failureMessage"] = failure.Message
        };

        return merged;
    }

    private static ScrollingCaptureResult WithDiagnostic(
        ScrollingCaptureResult result,
        string key,
        string value)
    {
        var diagnostics = new Dictionary<string, string>(result.Diagnostics)
        {
            [key] = value
        };

        return result with { Diagnostics = diagnostics };
    }

    private static ScrollingCaptureFailure? ValidateRequest(ScrollingCaptureRequest request)
    {
        if (request.MaximumFrames <= 0)
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling capture must allow at least one frame.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling capture requires an output directory.");
        }

        if (request.MinimumOverlapRatio <= 0 || request.MinimumOverlapRatio >= 1)
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling capture overlap ratio must be between zero and one.");
        }

        return null;
    }

    private static ScrollingCaptureFailure CreateScrollFailure(ScrollInputResult scrollResult)
    {
        ScrollInputFailure? failure = scrollResult.Failure;
        ScrollingCaptureFailureReason reason = failure?.Reason switch
        {
            ScrollInputFailureReason.TargetUnavailable => ScrollingCaptureFailureReason.TargetUnavailable,
            ScrollInputFailureReason.Unsupported => ScrollingCaptureFailureReason.Unsupported,
            ScrollInputFailureReason.CannotScroll => ScrollingCaptureFailureReason.ScrollFailed,
            _ => ScrollingCaptureFailureReason.ScrollFailed
        };

        return new ScrollingCaptureFailure(
            reason,
            failure?.Message ?? "The selected target could not be scrolled.",
            failure?.Exception);
    }
}
