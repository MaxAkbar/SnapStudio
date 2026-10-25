using Microsoft.UI.Xaml;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private bool _showCanvasGrid;
    private bool _snapToCanvasGrid = true;
    private double _canvasGridCellSize = CanvasGridLayout.DefaultSpacing;

    public bool ShowCanvasGrid
    {
        get => _showCanvasGrid;
        set { if (_showCanvasGrid == value) return; _showCanvasGrid = value; OnPropertyChanged(string.Empty); }
    }
    public bool SnapToCanvasGrid
    {
        get => _snapToCanvasGrid;
        set { if (_snapToCanvasGrid == value) return; _snapToCanvasGrid = value; OnPropertyChanged(nameof(SnapToCanvasGrid)); }
    }
    public double CanvasGridCellSize
    {
        get => _canvasGridCellSize;
        set { _canvasGridCellSize = CanvasGridLayout.NormalizeSpacing(value); OnPropertyChanged(string.Empty); }
    }
    public Visibility CanvasGridVisibility => ShowCanvasGrid ? Visibility.Visible : Visibility.Collapsed;
    private CanvasGridLayout CurrentCanvasGrid => CanvasGridLayout.Create(CanvasGridCellSize, CanvasZoom);
    public string CanvasGridSpacingDescription => CurrentCanvasGrid.Stride == 1 ? "Spacing is measured in image pixels."
        : $"At this zoom, visible lines are {CurrentCanvasGrid.SourceSpacing:0} image pixels apart. Zoom in for a finer grid.";
    private double EffectiveSnapGridSpacing => ShowCanvasGrid && SnapToCanvasGrid ? CurrentCanvasGrid.SourceSpacing : 0;
}
