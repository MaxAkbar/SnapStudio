using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsImageCreationService : IImageCreationService
{
    private readonly string _sourceImageDirectory;
    private readonly Func<CancellationToken, Task<Stream?>> _readClipboardImage;

    public WindowsImageCreationService(
        string sourceImageDirectory,
        Func<CancellationToken, Task<Stream?>>? readClipboardImage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImageDirectory);
        _sourceImageDirectory = Path.GetFullPath(sourceImageDirectory);
        _readClipboardImage = readClipboardImage ?? OpenClipboardImageStreamAsync;
    }

    public async Task<CaptureOutcome> CreateBlankAsync(
        NewImageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (NewImageRequest.GetValidationError(request.Width, request.Height) is string error)
        {
            return Failure(error);
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = new Bitmap(request.Width, request.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                }

                return SaveImage(bitmap, "new", "Untitled image", cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure($"The new image could not be created: {exception.Message}", exception);
        }
    }

    public async Task<CaptureOutcome> PasteAsync(CancellationToken cancellationToken)
    {
        ClipboardImageResult clipboard = await ReadClipboardImageAsync(cancellationToken).ConfigureAwait(true);
        if (clipboard.Image is not AnnotationImage image)
        {
            return Failure(clipboard.ErrorMessage ?? "The clipboard image could not be pasted.", clipboard.Exception);
        }

        try
        {
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(image.PngBytes, writable: false);
                using var bitmap = new Bitmap(stream);
                return SaveImage(bitmap, "clipboard", "Pasted image", cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure($"The clipboard image could not be pasted: {exception.Message}", exception);
        }
    }

    public async Task<ClipboardImageResult> ReadClipboardImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Clipboard access must start on the UI thread. Only decoding and saving run in the background.
            using Stream? stream = await _readClipboardImage(cancellationToken).ConfigureAwait(true);
            if (stream is null)
            {
                return new ClipboardImageResult(null, "The clipboard does not contain an image. Copy an image and try Paste again.");
            }

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = new Bitmap(stream);
                if (NewImageRequest.GetValidationError(bitmap.Width, bitmap.Height) is string error)
                {
                    return new ClipboardImageResult(null, error);
                }

                using var encoded = new MemoryStream();
                bitmap.Save(encoded, ImageFormat.Png);
                cancellationToken.ThrowIfCancellationRequested();
                return new ClipboardImageResult(new AnnotationImage(bitmap.Width, bitmap.Height, encoded.ToArray()), null);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ClipboardImageResult(null, $"The clipboard image could not be pasted: {exception.Message}", exception);
        }
    }

    private CaptureOutcome SaveImage(
        Bitmap bitmap,
        string source,
        string title,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_sourceImageDirectory);
        string path = Path.Combine(_sourceImageDirectory, $"{source}-{Guid.NewGuid():N}.png");
        try
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                bitmap.Save(output, ImageFormat.Png);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return CaptureOutcome.Success(new CaptureResult(
                CaptureId.New(),
                DateTimeOffset.UtcNow,
                new ImageAsset(path, bitmap.Width, bitmap.Height, ImagePixelFormat.Bgra32),
                new Dictionary<string, string> { ["title"] = title, ["source"] = source }));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static async Task<Stream?> OpenClipboardImageStreamAsync(CancellationToken cancellationToken)
    {
        DataPackageView content = Clipboard.GetContent();
        if (content.Contains(WindowsClipboardService.PngClipboardFormat))
        {
            object data = await content.GetDataAsync(WindowsClipboardService.PngClipboardFormat)
                .AsTask(cancellationToken).ConfigureAwait(true);
            if (data is IRandomAccessStream png)
            {
                png.Seek(0);
                return png.AsStreamForRead();
            }
        }

        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            return null;
        }

        var reference = await content.GetBitmapAsync().AsTask(cancellationToken).ConfigureAwait(true);
        var stream = await reference.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(true);
        return stream.AsStreamForRead();
    }

    private static CaptureOutcome Failure(string message, Exception? exception = null) =>
        CaptureOutcome.Failed(new CaptureFailure(CaptureFailureReason.Unsupported, message, exception));
}
