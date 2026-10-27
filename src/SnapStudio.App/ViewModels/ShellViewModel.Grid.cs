using Microsoft.UI.Xaml;
using SnapStudio.Core.Rendering;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private bool _showCanvasGrid;
    private bool _snapToCanvasGrid = true;
    private double _canvasGridCellSize = CanvasGridLayout.DefaultSpacing;
    private PerspectiveGridSettings _perspectiveGrid = new();
    private CanvasGridFamilies _gridFamilies = new CanvasGridFamilies().Normalize();
    private (double Width, double Height, PerspectiveGridSettings Settings, CanvasGridFamilies Families)? _perspectiveCacheKey;
    private IReadOnlyList<PerspectiveGridStroke> _perspectiveCache = [];

    public PerspectiveGridSettings CanvasGridPerspective { get => _perspectiveGrid; set => SetPerspectiveGrid(value); }
    public CanvasGridFamilies GridFamilies { get => _gridFamilies; set => SetGridFamilies(value); }
    public int ActivePerspectiveToolIndex => IsPerspectiveLineToolActive ? 1 : IsPerspectiveRectangleToolActive ? 2 : 0;
    public int PerspectiveRectanglePlaneIndex => (int)RectanglePerspectivePlane;
    public GridGuideSettings BaseGridStyle { get => _gridFamilies.Get(GridGuideFamily.Grid); set => SetGridFamilies(_gridFamilies with { Grid = value }); }
    public GridGuideSettings CenterGridStyle { get => _gridFamilies.Get(GridGuideFamily.Center); set => SetGridFamilies(_gridFamilies with { Center = value }); }
    public GridGuideSettings LeftGridStyle { get => _gridFamilies.Get(GridGuideFamily.Left); set => SetGridFamilies(_gridFamilies with { Left = value }); }
    public GridGuideSettings RightGridStyle { get => _gridFamilies.Get(GridGuideFamily.Right); set => SetGridFamilies(_gridFamilies with { Right = value }); }
    public GridGuideSettings TopGridStyle { get => _gridFamilies.Get(GridGuideFamily.Top); set => SetGridFamilies(_gridFamilies with { Top = value }); }
    public GridGuideSettings BottomGridStyle { get => _gridFamilies.Get(GridGuideFamily.Bottom); set => SetGridFamilies(_gridFamilies with { Bottom = value }); }
    public Visibility CenterGridFamilyVisibility => CanvasGridMode is 1 or 5 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SideGridFamiliesVisibility => CanvasGridMode >= 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TopGridFamilyVisibility => CanvasGridMode >= 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BottomGridFamilyVisibility => CanvasGridMode >= 4 ? Visibility.Visible : Visibility.Collapsed;
    public string CenterGridFamilyTitle => CanvasGridMode == 1 ? "VP 1" : "Center VP";
    public string TopGridFamilyTitle => CanvasGridMode == 3 ? "Third VP" : "Top VP";
    public void SetGridFamilies(CanvasGridFamilies families)
    {
        families = families.Normalize(); if (families == _gridFamilies) return;
        _gridFamilies = families; OnPropertyChanged(string.Empty);
    }
    public void ResetGridFamilies() => SetGridFamilies(new());
    public int CanvasGridMode { get => (int)_perspectiveGrid.Mode; set { if (value == CanvasGridMode) return; SetPerspectiveGrid(_perspectiveGrid with { Mode = (CanvasGridMode)value }); ShowCanvasGrid = true; } }
    public string CanvasGridModeIcon => $"Grid{CanvasGridMode}";
    public Visibility RectangularGridSettingsVisibility => CanvasGridMode == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PerspectiveGridSettingsVisibility => CanvasGridMode != 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PerspectiveHorizontalPointsVisibility => CanvasGridMode is >= 2 and <= 4 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PerspectiveCenterVisibility => CanvasGridMode == 2 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PerspectiveThirdPointVisibility => CanvasGridMode == 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PerspectiveRadiusVisibility => CanvasGridMode >= 4 ? Visibility.Visible : Visibility.Collapsed;
    public double PerspectiveCenterX { get => _perspectiveGrid.CenterX; set => SetPerspectiveGrid(_perspectiveGrid with { CenterX = value }); }
    public double PerspectiveHorizon { get => _perspectiveGrid.Horizon; set => SetPerspectiveGrid(_perspectiveGrid with { Horizon = value }); }
    public double PerspectiveLeft { get => _perspectiveGrid.Left; set => SetPerspectiveGrid(_perspectiveGrid with { Left = value }); }
    public double PerspectiveRight { get => _perspectiveGrid.Right; set => SetPerspectiveGrid(_perspectiveGrid with { Right = value }); }
    public double PerspectiveVertical { get => _perspectiveGrid.Vertical; set => SetPerspectiveGrid(_perspectiveGrid with { Vertical = value }); }
    public double PerspectiveRadius { get => _perspectiveGrid.Radius; set => SetPerspectiveGrid(_perspectiveGrid with { Radius = value }); }
    public void SetPerspectiveGrid(PerspectiveGridSettings settings)
    {
        settings = settings.Normalize(); if (settings == _perspectiveGrid) return;
        _perspectiveGrid = settings; OnPropertyChanged(string.Empty);
    }
    public void ResetPerspectiveGrid() => SetPerspectiveGrid(new(Mode: _perspectiveGrid.Mode));
    private IReadOnlyList<PerspectiveGridStroke> CurrentPerspectiveGrid
    {
        get
        {
            if (_currentDocument is null) return [];
            double width = _currentDocument.SourceImage.Width, height = _currentDocument.SourceImage.Height;
            var key = (width, height, _perspectiveGrid, _gridFamilies);
            if (_perspectiveCacheKey != key) { _perspectiveCache = PerspectiveGrid.Create(new RectD(0, 0, width, height), _perspectiveGrid, _gridFamilies); _perspectiveCacheKey = key; }
            return _perspectiveCache;
        }
    }
    private bool IsPerspectiveGridSnapEnabled => ShowCanvasGrid && SnapToCanvasGrid && CanvasGridMode != 0;

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
    public string CanvasGridSpacingDescription => CanvasGridMode != 0
        ? "Perspective guides use density. Positions are percentages of the image; vanishing points may be outside it. Guides position objects without warping their artwork."
        : CurrentCanvasGrid.Stride == 1 ? "Spacing is measured in image pixels."
        : $"At this zoom, visible lines are {CurrentCanvasGrid.SourceSpacing:0} image pixels apart. Zoom in for a finer grid.";
    private double EffectiveSnapGridSpacing => ShowCanvasGrid && SnapToCanvasGrid && CanvasGridMode == 0 && BaseGridStyle.Opacity > 0 ? CurrentCanvasGrid.SourceSpacing : 0;
}
