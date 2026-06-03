using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Storage;

public sealed class FileSystemDocumentThumbnailCache : IDocumentThumbnailCache
{
    private readonly IDocumentRenderer _documentRenderer;
    private readonly IDocumentRepository _documentRepository;
    private readonly string _rootPath;

    public FileSystemDocumentThumbnailCache(
        string rootPath,
        IDocumentRepository documentRepository,
        IDocumentRenderer documentRenderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(documentRepository);
        ArgumentNullException.ThrowIfNull(documentRenderer);

        _rootPath = Path.GetFullPath(rootPath);
        _documentRepository = documentRepository;
        _documentRenderer = documentRenderer;
    }

    public async Task<DocumentThumbnailResult> EnsureThumbnailAsync(
        DocumentThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.MaximumPixelSize <= 0)
        {
            return DocumentThumbnailResult.Failed("Thumbnail size must be greater than zero.");
        }

        string documentDirectory = GetDocumentDirectory(request.DocumentId);
        string thumbnailPath = GetThumbnailPath(request.DocumentId, request.ModifiedAtUtc);
        if (File.Exists(thumbnailPath))
        {
            return DocumentThumbnailResult.Success(thumbnailPath, wasGenerated: false);
        }

        CaptureDocument? document = await _documentRepository
            .GetAsync(request.DocumentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return DocumentThumbnailResult.Failed("The selected document could not be found.");
        }

        if (document.SourceImage.Width <= 0 || document.SourceImage.Height <= 0)
        {
            return DocumentThumbnailResult.Failed("The selected document has no renderable image size.");
        }

        double scale = CalculateScale(document, request.MaximumPixelSize);
        RenderResult renderResult = await _documentRenderer
            .RenderAsync(new RenderRequest(document.Id, scale, null), cancellationToken)
            .ConfigureAwait(false);

        if (!renderResult.Succeeded || renderResult.Image is not RenderedImage image)
        {
            return DocumentThumbnailResult.Failed(
                renderResult.ErrorMessage ?? "The thumbnail could not be rendered.");
        }

        try
        {
            Directory.CreateDirectory(documentDirectory);
            string temporaryPath = $"{thumbnailPath}.{Guid.NewGuid():N}.tmp";

            await File
                .WriteAllBytesAsync(temporaryPath, image.Pixels, cancellationToken)
                .ConfigureAwait(false);

            File.Move(temporaryPath, thumbnailPath, overwrite: true);
            File.SetLastWriteTimeUtc(thumbnailPath, request.ModifiedAtUtc.UtcDateTime);
            DeleteStaleThumbnails(documentDirectory, thumbnailPath);

            return DocumentThumbnailResult.Success(thumbnailPath, wasGenerated: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DocumentThumbnailResult.Failed(
                $"The thumbnail could not be cached: {exception.Message}");
        }
    }

    private string GetDocumentDirectory(DocumentId id) => Path.Combine(_rootPath, id.ToString());

    private string GetThumbnailPath(
        DocumentId id,
        DateTimeOffset modifiedAtUtc) => Path.Combine(
        GetDocumentDirectory(id),
        FileSystemDocumentNames.CreateThumbnailFileName(modifiedAtUtc));

    private static void DeleteStaleThumbnails(
        string documentDirectory,
        string currentThumbnailPath)
    {
        foreach (string thumbnailPath in Directory.EnumerateFiles(
            documentDirectory,
            FileSystemDocumentNames.ThumbnailFileSearchPattern))
        {
            if (string.Equals(thumbnailPath, currentThumbnailPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(thumbnailPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static double CalculateScale(CaptureDocument document, int maximumPixelSize)
    {
        int longestEdge = Math.Max(document.SourceImage.Width, document.SourceImage.Height);
        return Math.Min(1, maximumPixelSize / (double)longestEdge);
    }
}
