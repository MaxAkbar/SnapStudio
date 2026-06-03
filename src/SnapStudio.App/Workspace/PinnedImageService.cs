using SnapStudio.Core.Rendering;
using SnapStudio.Core.System;

namespace SnapStudio.App.Workspace;

internal sealed class PinnedImageService : IPinnedImageService
{
    private readonly IDocumentRenderer _documentRenderer;
    private readonly List<PinnedImageWindow> _windows = [];

    public PinnedImageService(IDocumentRenderer documentRenderer)
    {
        ArgumentNullException.ThrowIfNull(documentRenderer);

        _documentRenderer = documentRenderer;
    }

    public async Task<PinnedImageResult> PinDocumentAsync(
        PinnedImageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RenderResult renderResult = await _documentRenderer
            .RenderAsync(new RenderRequest(request.DocumentId, 1, null), cancellationToken)
            .ConfigureAwait(true);

        if (!renderResult.Succeeded || renderResult.Image is not RenderedImage image)
        {
            return PinnedImageResult.Failed(
                renderResult.ErrorMessage ?? "The current image could not be pinned.");
        }

        try
        {
            string imagePath = await WritePinnedImageAsync(image, cancellationToken)
                .ConfigureAwait(true);
            var window = new PinnedImageWindow(request.Title, imagePath, image);
            window.Closed += (_, _) => _windows.Remove(window);
            _windows.Add(window);
            window.Activate();

            return PinnedImageResult.Success();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or UriFormatException)
        {
            return PinnedImageResult.Failed(
                $"The current image could not be pinned: {exception.Message}");
        }
    }

    private static async Task<string> WritePinnedImageAsync(
        RenderedImage image,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SnapStudio", "PinnedImages");
        Directory.CreateDirectory(directory);

        string imagePath = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
        await File
            .WriteAllBytesAsync(imagePath, image.Pixels, cancellationToken)
            .ConfigureAwait(true);

        return imagePath;
    }
}
