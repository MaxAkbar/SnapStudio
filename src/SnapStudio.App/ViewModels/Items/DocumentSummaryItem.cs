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
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string TagsLabel => string.Join("  ", Tags.Select(tag => $"#{tag}"));
    public Visibility TagsVisibility => Tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

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
        string sourceLabel = CreateSourceLabel(summary.SourceKind);

        string? resolvedThumbnailPath = thumbnailPath ?? summary.ThumbnailPath;
        return new DocumentSummaryItem(
            summary.Id.ToString(),
            summary.Title,
            summary.ModifiedAtUtc.LocalDateTime.ToString("g"),
            CreateDetail(summary),
            NormalizeSourceKind(summary.SourceKind),
            sourceLabel,
            summary.SourceImagePath,
            resolvedThumbnailPath) { Tags = summary.Tags };
    }

    private static string CreateSourceLabel(string sourceKind)
    {
        return NormalizeSourceKind(sourceKind) switch
        {
            "capture" => "Capture",
            "import" => "Import",
            "clipboard" => "Clipboard",
            "new" => "New image",
            "scrollingcapture" => "Scrolling",
            "duplicate" => "Duplicate",
            "seed" => "Seed",
            _ => "Other"
        };
    }

    internal static string NormalizeSourceKind(string sourceKind)
    {
        return string.IsNullOrWhiteSpace(sourceKind)
            ? "capture"
            : sourceKind.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    internal static bool MatchesSearch(DocumentSummary summary, string searchText)
    {
        if (searchText.StartsWith('#'))
            return summary.Tags.Any(tag => string.Equals(tag, searchText[1..].Trim(), StringComparison.OrdinalIgnoreCase));
        return new[] { summary.Title, CreateDetail(summary), CreateSourceLabel(summary.SourceKind),
            NormalizeSourceKind(summary.SourceKind), summary.ModifiedAtUtc.LocalDateTime.ToString("g"), Path.GetFileName(summary.SourceImagePath) }
            .Any(value => value?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
            || summary.Tags.Any(tag => tag.Contains(searchText, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateDetail(DocumentSummary summary) =>
        $"{CreateSourceLabel(summary.SourceKind)} - {(summary.AnnotationCount == 1 ? "1 annotation" : $"{summary.AnnotationCount} annotations")}";

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
