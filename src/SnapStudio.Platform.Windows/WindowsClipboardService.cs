using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsClipboardService : IClipboardService
{
    internal const string PngClipboardFormat = "PNG";
    private readonly IDocumentRenderer _documentRenderer;
    private readonly string _temporaryDirectory;

    public WindowsClipboardService(
        IDocumentRenderer documentRenderer,
        string? temporaryDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(documentRenderer);

        _documentRenderer = documentRenderer;
        _temporaryDirectory = Path.GetFullPath(temporaryDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "SnapStudio",
            "Clipboard"));
    }

    public Task<ClipboardResult> CopyDocumentAsync(
        DocumentId documentId,
        CancellationToken cancellationToken) => CopyDocumentAsync(documentId, null, cancellationToken);

    public async Task<ClipboardResult> CopyDocumentAsync(
        DocumentId documentId,
        RectD? sourceBounds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            RenderResult renderResult = await _documentRenderer
                .RenderAsync(new RenderRequest(documentId, 1, sourceBounds), cancellationToken)
                .ConfigureAwait(true);

            if (!renderResult.Succeeded || renderResult.Image is not RenderedImage image)
                return ClipboardResult.Failed(renderResult.ErrorMessage ?? "The document could not be rendered.");

            Directory.CreateDirectory(_temporaryDirectory);
            string temporaryPath = Path.Combine(_temporaryDirectory, $"{documentId.Value:N}.png");
            await File
                .WriteAllBytesAsync(temporaryPath, image.Pixels, cancellationToken)
                .ConfigureAwait(true);

            StorageFile file = await StorageFile
                .GetFileFromPathAsync(temporaryPath)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);

            using IRandomAccessStreamWithContentType png = await file.OpenReadAsync()
                .AsTask(cancellationToken).ConfigureAwait(true);
            var data = new DataPackage();
            data.SetData(PngClipboardFormat, png);
            data.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Clipboard.SetContent(data);
            Clipboard.Flush();

            return ClipboardResult.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ClipboardResult.Failed($"The image could not be copied to the clipboard: {exception.Message}");
        }
    }

    public Task<ClipboardResult> CopyTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
            Clipboard.Flush();

            return Task.FromResult(ClipboardResult.Success());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(ClipboardResult.Failed(
                $"The text could not be copied to the clipboard: {exception.Message}"));
        }
    }
}
