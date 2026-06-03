using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Diagnostics;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Storage;

public sealed class FileSystemScrollingCaptureDiagnosticsBundleWriter : IScrollingCaptureDiagnosticsBundleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ISensitiveDataRedactor _redactor;

    public FileSystemScrollingCaptureDiagnosticsBundleWriter(ISensitiveDataRedactor? redactor = null)
    {
        _redactor = redactor ?? new DefaultSensitiveDataRedactor();
    }

    public async Task<ScrollingCaptureDiagnosticsBundleResult> WriteAsync(
        ScrollingCaptureDiagnosticsBundleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.CaptureRequest.OutputDirectory))
        {
            return ScrollingCaptureDiagnosticsBundleResult.Failed(
                "Scrolling capture diagnostics bundle requires an output directory.");
        }

        try
        {
            string outputDirectory = Path.GetFullPath(request.CaptureRequest.OutputDirectory);
            string bundleDirectory = Path.Combine(outputDirectory, "diagnostics");
            Directory.CreateDirectory(bundleDirectory);
            string bundlePath = Path.Combine(
                bundleDirectory,
                $"scrolling-capture-{DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.json");

            ScrollingCaptureDiagnosticsBundle bundle = CreateBundle(request);
            string json = JsonSerializer.Serialize(bundle, JsonOptions);
            await File.WriteAllTextAsync(bundlePath, json, cancellationToken).ConfigureAwait(false);

            return ScrollingCaptureDiagnosticsBundleResult.Success(bundlePath);
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
            return ScrollingCaptureDiagnosticsBundleResult.Failed(
                $"Scrolling capture diagnostics bundle could not be written: {exception.Message}");
        }
    }

    private ScrollingCaptureDiagnosticsBundle CreateBundle(
        ScrollingCaptureDiagnosticsBundleRequest request)
    {
        ScrollingCaptureRequest captureRequest = request.CaptureRequest;
        ScrollingCaptureResult result = request.CaptureResult;

        return new ScrollingCaptureDiagnosticsBundle(
            SchemaVersion: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Status: ResolveStatus(result),
            Request: new ScrollingCaptureDiagnosticsRequest(
                MaximumFrames: captureRequest.MaximumFrames,
                MinimumOverlapRatio: captureRequest.MinimumOverlapRatio,
                OutputDirectory: Redact(captureRequest.OutputDirectory)),
            Target: new ScrollingCaptureDiagnosticsTarget(
                Id: Redact(captureRequest.Target.Id),
                DisplayName: Redact(captureRequest.Target.DisplayName),
                Kind: captureRequest.Target.Kind.ToString(),
                Bounds: captureRequest.Target.Bounds,
                Metadata: Redact(captureRequest.Target.Metadata)),
            OutputImage: result.Image is null ? null : CreateImage(result.Image),
            Failure: result.Failure is null ? null : CreateFailure(result.Failure),
            FrameCount: result.Frames.Count,
            Frames: result.Frames.Select(CreateFrame).ToArray(),
            Diagnostics: Redact(result.Diagnostics));
    }

    private ScrollingCaptureDiagnosticsFrame CreateFrame(ScrollingCaptureFrame frame)
    {
        return new ScrollingCaptureDiagnosticsFrame(
            Index: frame.Index,
            Image: CreateImage(frame.Image),
            TargetBounds: frame.TargetBounds,
            ScrollOffset: frame.ScrollOffset,
            Metadata: Redact(frame.Metadata));
    }

    private ScrollingCaptureDiagnosticsImage CreateImage(ImageAsset image)
    {
        return new ScrollingCaptureDiagnosticsImage(
            Path: Redact(image.Path),
            FileName: Redact(Path.GetFileName(image.Path)),
            Width: image.Width,
            Height: image.Height,
            PixelFormat: image.PixelFormat.ToString());
    }

    private ScrollingCaptureDiagnosticsFailure CreateFailure(ScrollingCaptureFailure failure)
    {
        return new ScrollingCaptureDiagnosticsFailure(
            Reason: failure.Reason.ToString(),
            Message: Redact(failure.Message),
            ExceptionType: failure.Exception?.GetType().FullName);
    }

    private string Redact(string value)
    {
        return _redactor.Redact(value);
    }

    private IReadOnlyDictionary<string, string> Redact(IReadOnlyDictionary<string, string> properties)
    {
        return _redactor.RedactProperties(properties);
    }

    private static string ResolveStatus(ScrollingCaptureResult result)
    {
        if (result.Succeeded)
        {
            return "succeeded";
        }

        return result.HasOutput || result.IsPartial
            ? "partial"
            : "failed";
    }

    private sealed record ScrollingCaptureDiagnosticsBundle(
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string Status,
        ScrollingCaptureDiagnosticsRequest Request,
        ScrollingCaptureDiagnosticsTarget Target,
        ScrollingCaptureDiagnosticsImage? OutputImage,
        ScrollingCaptureDiagnosticsFailure? Failure,
        int FrameCount,
        IReadOnlyList<ScrollingCaptureDiagnosticsFrame> Frames,
        IReadOnlyDictionary<string, string> Diagnostics);

    private sealed record ScrollingCaptureDiagnosticsRequest(
        int MaximumFrames,
        double MinimumOverlapRatio,
        string OutputDirectory);

    private sealed record ScrollingCaptureDiagnosticsTarget(
        string Id,
        string DisplayName,
        string Kind,
        RectD Bounds,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record ScrollingCaptureDiagnosticsImage(
        string Path,
        string FileName,
        int Width,
        int Height,
        string PixelFormat);

    private sealed record ScrollingCaptureDiagnosticsFrame(
        int Index,
        ScrollingCaptureDiagnosticsImage Image,
        RectD TargetBounds,
        double ScrollOffset,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record ScrollingCaptureDiagnosticsFailure(
        string Reason,
        string Message,
        string? ExceptionType);
}
