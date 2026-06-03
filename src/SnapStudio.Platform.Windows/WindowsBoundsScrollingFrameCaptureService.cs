using System.Globalization;
using System.Runtime.InteropServices;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsBoundsScrollingFrameCaptureService : IScrollingFrameCaptureService
{
    private readonly IWindowsScrollingFrameImageWriter _imageWriter;

    public WindowsBoundsScrollingFrameCaptureService()
        : this(new SystemDrawingScrollingFrameImageWriter())
    {
    }

    public WindowsBoundsScrollingFrameCaptureService(IWindowsScrollingFrameImageWriter imageWriter)
    {
        ArgumentNullException.ThrowIfNull(imageWriter);

        _imageWriter = imageWriter;
    }

    public Task<ScrollingFrameCaptureResult> CaptureFrameAsync(
        ScrollingFrameCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ScrollingCaptureFailure? validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
        {
            return Task.FromResult(ScrollingFrameCaptureResult.Failed(validationFailure));
        }

        return Task.Run(
            () => CaptureCore(request, cancellationToken),
            cancellationToken);
    }

    private ScrollingFrameCaptureResult CaptureCore(
        ScrollingFrameCaptureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            RectD normalizedBounds = Normalize(request.Target.Bounds);
            string outputDirectory = Path.GetFullPath(request.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(
                outputDirectory,
                $"scroll-frame-{request.FrameIndex.ToString("D4", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.png");

            var startedAtUtc = DateTimeOffset.UtcNow;
            ImageAsset image = _imageWriter.Capture(
                normalizedBounds,
                outputPath,
                cancellationToken);

            var frame = new ScrollingCaptureFrame(
                request.FrameIndex,
                image,
                normalizedBounds,
                request.FrameIndex,
                new Dictionary<string, string>
                {
                    ["captureMethod"] = "gdi-copy-from-screen",
                    ["capturedAtUtc"] = startedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["targetId"] = request.Target.Id,
                    ["targetDisplayName"] = request.Target.DisplayName
                });

            return ScrollingFrameCaptureResult.Success(frame);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or ExternalException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return ScrollingFrameCaptureResult.Failed(new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.FrameCaptureFailed,
                $"The scrolling capture frame could not be captured: {exception.Message}",
                exception));
        }
    }

    private static ScrollingCaptureFailure? ValidateRequest(ScrollingFrameCaptureRequest request)
    {
        if (request.FrameIndex < 0)
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling frame capture requires a non-negative frame index.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling frame capture requires an output directory.");
        }

        RectD bounds = Normalize(request.Target.Bounds);
        if (!IsFinite(bounds)
            || bounds.Width <= 1
            || bounds.Height <= 1)
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling frame capture requires target bounds larger than one pixel.");
        }

        return null;
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static bool IsFinite(RectD bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height);
    }
}
