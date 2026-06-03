using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.ViewModels.Items;

public sealed record DocumentSummaryItem(
    string Id,
    string Title,
    string ModifiedLabel,
    string Detail,
    string SourceKind,
    string SourceLabel,
    string SourceImagePath,
    string? ThumbnailPath)
{
    public BitmapImage? ThumbnailImageSource { get; } = CreateThumbnailImageSource(ThumbnailPath);

    public Visibility ThumbnailVisibility => ThumbnailImageSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ThumbnailFallbackVisibility => ThumbnailImageSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public static DocumentSummaryItem FromSummary(
        DocumentSummary summary,
        string? thumbnailPath = null)
    {
        string detail = summary.AnnotationCount == 1
            ? "1 annotation"
            : $"{summary.AnnotationCount} annotations";
        string sourceLabel = CreateSourceLabel(summary.SourceKind);

        string? resolvedThumbnailPath = thumbnailPath ?? summary.ThumbnailPath;
        return new DocumentSummaryItem(
            summary.Id.ToString(),
            summary.Title,
            summary.ModifiedAtUtc.LocalDateTime.ToString("g"),
            $"{sourceLabel} - {detail}",
            NormalizeSourceKind(summary.SourceKind),
            sourceLabel,
            summary.SourceImagePath,
            resolvedThumbnailPath);
    }

    private static string CreateSourceLabel(string sourceKind)
    {
        return NormalizeSourceKind(sourceKind) switch
        {
            "capture" => "Capture",
            "import" => "Import",
            "scrollingcapture" => "Scrolling",
            "duplicate" => "Duplicate",
            "seed" => "Seed",
            _ => "Other"
        };
    }

    private static string NormalizeSourceKind(string sourceKind)
    {
        return string.IsNullOrWhiteSpace(sourceKind)
            ? "capture"
            : sourceKind.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static BitmapImage? CreateThumbnailImageSource(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            return null;
        }

        try
        {
            return new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                UriSource = new Uri(Path.GetFullPath(thumbnailPath))
            };
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
