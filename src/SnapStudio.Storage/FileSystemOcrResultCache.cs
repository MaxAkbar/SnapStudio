using System.Text.Json;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Storage;

public sealed class FileSystemOcrResultCache : IOcrResultCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootPath;

    public FileSystemOcrResultCache(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<OcrCacheDocument> GetAsync(
        DocumentId documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string cachePath = GetCachePath(documentId);
        if (!File.Exists(cachePath))
        {
            return OcrCacheDocument.Empty(documentId);
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            OcrCacheDocument? cacheDocument = await JsonSerializer
                .DeserializeAsync<OcrCacheDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return cacheDocument is not null && cacheDocument.DocumentId == documentId
                ? cacheDocument
                : OcrCacheDocument.Empty(documentId);
        }
        catch (JsonException)
        {
            return OcrCacheDocument.Empty(documentId);
        }
        catch (IOException)
        {
            return OcrCacheDocument.Empty(documentId);
        }
        catch (UnauthorizedAccessException)
        {
            return OcrCacheDocument.Empty(documentId);
        }
    }

    public async Task SaveAsync(
        OcrCacheDocument cacheDocument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cacheDocument);
        cancellationToken.ThrowIfCancellationRequested();

        string documentDirectory = GetDocumentDirectory(cacheDocument.DocumentId);
        Directory.CreateDirectory(documentDirectory);

        string cachePath = GetCachePath(cacheDocument.DocumentId);
        string temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(cacheDocument, JsonOptions);

        await File
            .WriteAllTextAsync(temporaryPath, json, cancellationToken)
            .ConfigureAwait(false);

        File.Move(temporaryPath, cachePath, overwrite: true);
    }

    public Task<bool> DeleteAsync(
        DocumentId documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string cachePath = GetCachePath(documentId);
        if (!File.Exists(cachePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(cachePath);
        return Task.FromResult(true);
    }

    private string GetDocumentDirectory(DocumentId documentId) => Path.Combine(
        _rootPath,
        documentId.ToString());

    private string GetCachePath(DocumentId documentId) => Path.Combine(
        GetDocumentDirectory(documentId),
        FileSystemDocumentNames.OcrCacheFileName);
}
