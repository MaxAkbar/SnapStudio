using System.Runtime.InteropServices;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using CoreOcrResult = SnapStudio.Core.Ocr.OcrResult;
using WindowsOcrResult = Windows.Media.Ocr.OcrResult;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsOcrProvider : IOcrProvider
{
    private const string ProviderName = "Windows OCR";
    private readonly IClock _clock;

    public WindowsOcrProvider(IClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
    }

    public Task<OcrProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string[] languageTags = OcrEngine
                .AvailableRecognizerLanguages
                .Select(language => language.LanguageTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (languageTags.Length == 0)
            {
                return Task.FromResult(OcrProviderStatus.Unavailable(
                    ProviderName,
                    "No Windows OCR recognizer languages are installed."));
            }

            OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
            return Task.FromResult(engine is null
                ? OcrProviderStatus.Unavailable(
                    ProviderName,
                    "Windows OCR could not create an engine from the current user profile languages.")
                : OcrProviderStatus.Available(ProviderName, languageTags));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(OcrProviderStatus.Unavailable(
                ProviderName,
                $"Windows OCR is unavailable: {exception.Message}"));
        }
    }

    public async Task<OcrRecognitionResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.SourceImage.Path)
            || !File.Exists(request.SourceImage.Path))
        {
            return OcrRecognitionResult.Failed(new OcrFailure(
                OcrFailureReason.ImageUnavailable,
                "The source image for OCR could not be found."));
        }

        OcrEngine? engine = CreateEngine(request.LanguageTag);
        if (engine is null)
        {
            return OcrRecognitionResult.Failed(new OcrFailure(
                OcrFailureReason.ProviderUnavailable,
                string.IsNullOrWhiteSpace(request.LanguageTag)
                    ? "Windows OCR could not create an engine from the current user profile languages."
                    : $"Windows OCR does not support '{request.LanguageTag}'."));
        }

        if (!TryCreateBitmapTransform(
            request,
            out BitmapTransform transform,
            out RectD? recognizedRegion,
            out OcrFailure? transformFailure))
        {
            return OcrRecognitionResult.Failed(transformFailure ?? new OcrFailure(
                OcrFailureReason.ImageUnavailable,
                "The selected OCR region could not be decoded."));
        }

        try
        {
            using SoftwareBitmap bitmap = await LoadBitmapAsync(
                    request.SourceImage.Path,
                    transform,
                    cancellationToken)
                .ConfigureAwait(false);

            int maximumDimension = (int)OcrEngine.MaxImageDimension;
            if (bitmap.PixelWidth > maximumDimension || bitmap.PixelHeight > maximumDimension)
            {
                return OcrRecognitionResult.Failed(new OcrFailure(
                    OcrFailureReason.RecognitionFailed,
                    $"The OCR region is larger than the Windows OCR limit of {maximumDimension} pixels per edge."));
            }

            WindowsOcrResult windowsResult = await engine
                .RecognizeAsync(bitmap)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return OcrRecognitionResult.Success(new CoreOcrResult(
                Guid.NewGuid(),
                request.DocumentId,
                _clock.UtcNow,
                recognizedRegion,
                engine.RecognizerLanguage.LanguageTag,
                CreateLines(windowsResult, recognizedRegion)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is FileNotFoundException
            or DirectoryNotFoundException
            or UnauthorizedAccessException
            or IOException)
        {
            return OcrRecognitionResult.Failed(new OcrFailure(
                OcrFailureReason.ImageUnavailable,
                $"The source image for OCR could not be opened: {exception.Message}",
                exception));
        }
        catch (Exception exception) when (exception is ArgumentException
            or COMException
            or InvalidOperationException
            or NotSupportedException)
        {
            return OcrRecognitionResult.Failed(new OcrFailure(
                OcrFailureReason.RecognitionFailed,
                $"Windows OCR failed to recognize text: {exception.Message}",
                exception));
        }
    }

    private static OcrEngine? CreateEngine(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }

        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language(languageTag));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static async Task<SoftwareBitmap> LoadBitmapAsync(
        string sourcePath,
        BitmapTransform transform,
        CancellationToken cancellationToken)
    {
        StorageFile file = await StorageFile
            .GetFileFromPathAsync(sourcePath)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        using IRandomAccessStream stream = await file
            .OpenReadAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder
            .CreateAsync(stream)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return await decoder
            .GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryCreateBitmapTransform(
        OcrRequest request,
        out BitmapTransform transform,
        out RectD? recognizedRegion,
        out OcrFailure? failure)
    {
        transform = new BitmapTransform();
        recognizedRegion = null;
        failure = null;

        if (request.SourceRegion is null)
        {
            return true;
        }

        if (request.SourceImage.Width <= 0 || request.SourceImage.Height <= 0)
        {
            failure = new OcrFailure(
                OcrFailureReason.ImageUnavailable,
                "The source image has no OCR-readable dimensions.");
            return false;
        }

        RectD normalized = Normalize(request.SourceRegion.Value);
        int left = (int)Math.Clamp(Math.Floor(normalized.X), 0, request.SourceImage.Width);
        int top = (int)Math.Clamp(Math.Floor(normalized.Y), 0, request.SourceImage.Height);
        int right = (int)Math.Clamp(Math.Ceiling(normalized.Right), left, request.SourceImage.Width);
        int bottom = (int)Math.Clamp(Math.Ceiling(normalized.Bottom), top, request.SourceImage.Height);
        int width = right - left;
        int height = bottom - top;

        if (width <= 0 || height <= 0)
        {
            failure = new OcrFailure(
                OcrFailureReason.ImageUnavailable,
                "The selected OCR region is outside the source image.");
            return false;
        }

        transform.Bounds = new BitmapBounds
        {
            X = (uint)left,
            Y = (uint)top,
            Width = (uint)width,
            Height = (uint)height
        };
        recognizedRegion = new RectD(left, top, width, height);
        return true;
    }

    private static IReadOnlyList<OcrTextLine> CreateLines(
        WindowsOcrResult result,
        RectD? recognizedRegion)
    {
        double offsetX = recognizedRegion?.X ?? 0;
        double offsetY = recognizedRegion?.Y ?? 0;

        return result
            .Lines
            .Select(line =>
            {
                OcrTextWord[] words = line
                    .Words
                    .Select(word => new OcrTextWord(
                        word.Text,
                        ToSourceRect(word.BoundingRect, offsetX, offsetY),
                        null))
                    .ToArray();

                return new OcrTextLine(
                    line.Text,
                    CreateLineBounds(words),
                    words);
            })
            .ToArray();
    }

    private static RectD CreateLineBounds(IReadOnlyList<OcrTextWord> words)
    {
        if (words.Count == 0)
        {
            return new RectD(0, 0, 0, 0);
        }

        double left = words.Min(word => word.Bounds.X);
        double top = words.Min(word => word.Bounds.Y);
        double right = words.Max(word => word.Bounds.Right);
        double bottom = words.Max(word => word.Bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static RectD ToSourceRect(
        global::Windows.Foundation.Rect bounds,
        double offsetX,
        double offsetY)
    {
        return new RectD(
            bounds.X + offsetX,
            bounds.Y + offsetY,
            bounds.Width,
            bounds.Height);
    }

    private static RectD Normalize(RectD bounds)
    {
        double x = Math.Min(bounds.X, bounds.Right);
        double y = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(x, y, right - x, bottom - y);
    }
}
