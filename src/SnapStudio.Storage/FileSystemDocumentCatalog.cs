using System.Text.Json;
using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

public sealed class FileSystemDocumentCatalog : IDocumentCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _rootPath;

    public FileSystemDocumentCatalog(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<IReadOnlyList<DocumentSummary>> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0 || !Directory.Exists(_rootPath))
        {
            return [];
        }

        var summaries = new List<DocumentSummary>();

        foreach (string documentDirectory in Directory.EnumerateDirectories(_rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string documentPath = Path.Combine(
                documentDirectory,
                FileSystemDocumentNames.DocumentFileName);

            if (!File.Exists(documentPath))
            {
                continue;
            }

            CaptureDocument? document = await LoadDocumentAsync(documentPath, cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                continue;
            }

            summaries.Add(CreateSummary(document, documentDirectory));
        }

        return summaries
            .OrderByDescending(summary => summary.ModifiedAtUtc)
            .ThenByDescending(summary => summary.CreatedAtUtc)
            .Take(maximumCount)
            .ToArray();
    }

    private static async Task<CaptureDocument?> LoadDocumentAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(documentPath);
            return await JsonSerializer
                .DeserializeAsync<CaptureDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static DocumentSummary CreateSummary(
        CaptureDocument document,
        string documentDirectory)
    {
        string title = document.Metadata.Properties.TryGetValue("title", out string? value)
            ? value
            : $"Capture {document.Metadata.CreatedAtUtc:yyyy-MM-dd HH:mm}";
        string? thumbnailPath = ResolveThumbnailPath(
            documentDirectory,
            document.Metadata.ModifiedAtUtc);
        string sourceKind = ResolveSourceKind(document.Metadata.Properties);

        return new DocumentSummary(
            document.Id,
            title,
            document.Metadata.CreatedAtUtc,
            document.Metadata.ModifiedAtUtc,
            document.SourceImage.Path,
            document.Annotations.Count,
            thumbnailPath,
            sourceKind);
    }

    private static string ResolveSourceKind(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.ContainsKey("duplicatedFromDocumentId"))
        {
            return "duplicate";
        }

        if (!metadata.TryGetValue("source", out string? source)
            || string.IsNullOrWhiteSpace(source))
        {
            return "capture";
        }

        return source.Trim();
    }

    private static string? ResolveThumbnailPath(
        string documentDirectory,
        DateTimeOffset modifiedAtUtc)
    {
        string exactThumbnailPath = Path.Combine(
            documentDirectory,
            FileSystemDocumentNames.CreateThumbnailFileName(modifiedAtUtc));
        if (File.Exists(exactThumbnailPath))
        {
            return exactThumbnailPath;
        }

        try
        {
            return Directory
                .EnumerateFiles(documentDirectory, FileSystemDocumentNames.ThumbnailFileSearchPattern)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
