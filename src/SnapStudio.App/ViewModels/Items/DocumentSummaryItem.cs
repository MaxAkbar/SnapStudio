using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.ViewModels.Items;

public sealed class DocumentSummaryItem : INotifyPropertyChanged
{
    private DocumentSummary _summary;
    private string? _thumbnailPath;
    private int _thumbnailRevision;
    private DocumentSummaryItem(DocumentSummary summary)
    {
        _summary = summary;
        UpdateFromSummary(summary);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => _summary.Id.ToString();
    public override string ToString() => Title;
    public string Title => _summary.Title;
    public string ModifiedLabel => _summary.ModifiedAtUtc.LocalDateTime.ToString("g");
    public string Detail => CreateDetail(_summary);
    public string SourceKind => NormalizeSourceKind(_summary.SourceKind);
    public string SourceLabel => CreateSourceLabel(_summary.SourceKind);
    public string SourceImagePath => _summary.SourceImagePath;
    public string? ThumbnailPath => _thumbnailPath;
    public IReadOnlyList<string> Tags => _summary.Tags;
    public string TagsLabel => string.Join("  ", Tags.Select(tag => $"#{tag}"));
    public Visibility TagsVisibility => Tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public BitmapImage? ThumbnailImageSource { get; private set; }

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
        return new DocumentSummaryItem(summary with { ThumbnailPath = thumbnailPath ?? summary.ThumbnailPath });
    }

    internal void UpdateFromSummary(DocumentSummary summary)
    {
        bool changed = (_summary with { Tags = summary.Tags, ThumbnailPath = summary.ThumbnailPath }) != summary
            || !_summary.Tags.SequenceEqual(summary.Tags);
        _summary = summary;
        if (changed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        if (string.Equals(_thumbnailPath, summary.ThumbnailPath, StringComparison.OrdinalIgnoreCase)) return;
        _thumbnailPath = summary.ThumbnailPath;
        _ = LoadThumbnailAsync(_thumbnailPath, ++_thumbnailRevision);
    }

    private async Task LoadThumbnailAsync(string? path, int revision)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            using FileStream stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (_thumbnailRevision != revision) return;
            // Swap only decoded pixels. Keep the previous image visible during render/decode or on failure.
            ThumbnailImageSource = bitmap;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailImageSource)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailFallbackVisibility)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or System.Runtime.InteropServices.COMException)
        { }
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

}
