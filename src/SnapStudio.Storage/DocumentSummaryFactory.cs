using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

internal static class DocumentSummaryFactory
{
    public static DocumentSummary Create(
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

    public static string ResolveSourceKind(IReadOnlyDictionary<string, string> metadata)
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
