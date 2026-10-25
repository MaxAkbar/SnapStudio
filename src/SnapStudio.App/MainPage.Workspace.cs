using System.Globalization;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using Windows.UI;

namespace SnapStudio.App;

public sealed partial class MainPage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyWorkspacePresentationChanged()
    {
        foreach (string name in WorkspacePresentationProperties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static readonly string[] WorkspacePresentationProperties =
    [
        nameof(WorkspaceColorHex), nameof(WorkspaceStrokeBrush), nameof(WorkspaceOpacityLabel),
        nameof(WorkspaceAnnotationColor),
        nameof(WorkspaceObjectLabel), nameof(WorkspaceToolKind), nameof(WorkspaceToolHint),
        nameof(WorkspacePageLabel), nameof(WorkspaceRangeLabel), nameof(WorkspaceCaptureCount),
        nameof(WorkspaceFileType), nameof(WorkspaceSquareCornersBrush), nameof(WorkspaceRoundedCornersBrush)
    ];

    public string WorkspaceColorHex
    {
        get
        {
            var color = ViewModel.AnnotationPropertyColor;
            return ViewModel.HasMixedAnnotationColors ? "Mixed" : $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    public SolidColorBrush WorkspaceStrokeBrush
    {
        get
        {
            var color = ViewModel.AnnotationPropertyColor;
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }
    }

    public Color WorkspaceAnnotationColor
    {
        get
        {
            var color = ViewModel.AnnotationPropertyColor;
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }

    public string WorkspaceOpacityLabel => $"{Math.Round(ViewModel.AnnotationOpacity * 100).ToString(CultureInfo.CurrentCulture)}%";

    public string WorkspaceObjectLabel => ViewModel.AnnotationPropertiesTitle;

    public string WorkspaceToolKind => ViewModel.IsSelectionToolActive
        ? (ViewModel.IsMarqueeToolActive ? ViewModel.AreaSelectionIcon : "Move")
        : ViewModel.ActiveAnnotationKind.ToString();

    public string WorkspaceToolHint => ViewModel.IsSelectionToolActive
        ? (ViewModel.IsMarqueeToolActive ? ViewModel.AreaSelectionHint : "Click or drag objects · M to select an area")
        : ViewModel.ActiveAnnotationKind switch
        {
            AnnotationKind.Fill => "Click an object or image area to fill",
            AnnotationKind.Pen => "Drag on the canvas to draw",
            AnnotationKind.Text => "Click the canvas to add text",
            AnnotationKind.Blur => "Drag to blur an area",
            AnnotationKind.Highlight => "Drag to highlight an area",
            _ => $"Drag to draw a {ViewModel.ActiveAnnotationToolLabel.ToLowerInvariant()}"
        };

    public string WorkspacePageLabel => $"{ViewModel.HistoryPageNumber} / {ViewModel.HistoryPageCount}";

    public string WorkspaceRangeLabel
    {
        get
        {
            string label = ViewModel.HistoryRangeLabel;
            if (label.EndsWith(" captures", StringComparison.Ordinal)) return label[..^9];
            if (label.EndsWith(" capture", StringComparison.Ordinal)) return label[..^8];
            return label;
        }
    }

    public int WorkspaceCaptureCount => ViewModel.SelectedHistoryFolder?.Count ?? 0;

    public string WorkspaceFileType
    {
        get
        {
            string extension = System.IO.Path.GetExtension(ViewModel.CurrentDocumentFileNameLabel);
            return string.IsNullOrEmpty(extension) ? "IMAGE" : extension[1..].ToUpperInvariant();
        }
    }

    public SolidColorBrush WorkspaceSquareCornersBrush => WorkspaceCornerBrush(ViewModel.AnnotationCornerStyleIndex == 0);

    public SolidColorBrush WorkspaceRoundedCornersBrush => WorkspaceCornerBrush(ViewModel.AnnotationCornerStyleIndex == 1);

    private SolidColorBrush WorkspaceCornerBrush(bool selected) => selected
        ? (SolidColorBrush)Resources["SnapAccentMutedBrush"]
        : new(Color.FromArgb(0, 0, 0, 0));

    private async void WorkspaceCorners_Click(object sender, RoutedEventArgs e)
    {
        if (ShouldIgnoreControlEvent() || sender is not Button { Tag: string tag }
            || !int.TryParse(tag, out int index) || index is not (0 or 1)
            || index == ViewModel.AnnotationCornerStyleIndex) return;

        await ViewModel.UpdateAnnotationCornerStyleAsync(index, CancellationToken.None);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
    }

    private void WorkspaceFill_Click(object sender, RoutedEventArgs e)
    {
        if (ShouldIgnoreControlEvent()) return;
        ViewModel.UpdateAnnotationTool(8);
        PropertiesSectionExpander.IsExpanded = true;
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
    }
}
