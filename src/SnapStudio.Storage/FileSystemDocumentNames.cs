namespace SnapStudio.Storage;

internal static class FileSystemDocumentNames
{
    public const string DocumentFileName = "document.snapstudio.json";

    public const string OcrCacheFileName = "ocr-cache.snapstudio.json";

    public const string ThumbnailFileSearchPattern = "thumbnail-*.png";

    public static string CreateThumbnailFileName(DateTimeOffset modifiedAtUtc)
    {
        return $"thumbnail-{modifiedAtUtc.ToUniversalTime().UtcTicks}.png";
    }
}
