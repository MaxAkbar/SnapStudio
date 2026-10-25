using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private PixelSelectionTool _pixelSelectionTool;
    private int _pixelSelectionRequestVersion;
    private bool _pixelSelectionBusy;
    private double _wandTolerance = 16;

    public PixelSelection? SourcePixelSelection => _annotationSelection.PixelMask;
    public PixelSelectionTool AreaSelectionTool => _pixelSelectionTool;
    public int PixelSelectionToolIndex
    {
        get => (int)_pixelSelectionTool;
        set
        {
            if (value is < 0 or > 4) return;
            if (value == (int)_pixelSelectionTool && IsMarqueeToolActive) return;
            _pixelSelectionTool = (PixelSelectionTool)value;
            _pixelSelectionRequestVersion++;
            UseSelectionTool();
            NotifyPropertyContextChanged();
        }
    }
    public int PixelSelectionModeIndex { get; set; }
    public PixelSelectionMode AreaSelectionMode => (PixelSelectionMode)Math.Clamp(PixelSelectionModeIndex, 0, 3);
    public bool WandContiguous { get; set; } = true;
    public double WandTolerance
    {
        get => _wandTolerance;
        set => _wandTolerance = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 0, 255) : 16;
    }
    public Visibility PixelSelectionControlsVisibility => IsMarqueeToolActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WandControlsVisibility => IsMarqueeToolActive && AreaSelectionTool == PixelSelectionTool.MagicWand ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PixelSelectionActionsVisibility => SourcePixelSelection is not null ? Visibility.Visible : Visibility.Collapsed;
    public string AreaSelectionToolName => AreaSelectionTool switch
    {
        PixelSelectionTool.Ellipse => "Elliptical marquee",
        PixelSelectionTool.Lasso => "Lasso",
        PixelSelectionTool.MagicWand => "Magic wand",
        PixelSelectionTool.PolygonalLasso => "Polygonal lasso",
        _ => "Rectangular marquee"
    };
    public string AreaSelectionIcon => AreaSelectionTool switch
    {
        PixelSelectionTool.Ellipse => "SelectEllipse",
        PixelSelectionTool.Lasso => "Lasso",
        PixelSelectionTool.MagicWand => "MagicWand",
        PixelSelectionTool.PolygonalLasso => "PolygonalLasso",
        _ => "Select"
    };
    public string AreaSelectionHint => AreaSelectionTool switch
    {
        PixelSelectionTool.Lasso => "Drag a freehand outline; release to close it",
        PixelSelectionTool.MagicWand => "Click a color to select similar pixels",
        PixelSelectionTool.PolygonalLasso => "Click corners for straight edges; double-click, click the first corner, or press Enter to close",
        _ => "Drag to select an area · Shift constrains proportions"
    };

    public async Task SelectPixelAreaAsync(RectD displayBounds, IReadOnlyList<PointD>? displayPoints,
        PixelSelectionMode mode, CancellationToken token)
    {
        if (_currentDocument is not { } document || !IsMarqueeToolActive) return;
        int version = ++_pixelSelectionRequestVersion;
        int changeVersion = _documentChangeVersion;
        PixelSelection? original = SourcePixelSelection;
        PixelSelectionTool tool = AreaSelectionTool;
        double zoom = CanvasZoom;
        RectD source = new(displayBounds.X / zoom, displayBounds.Y / zoom, displayBounds.Width / zoom, displayBounds.Height / zoom);
        PointD[]? points = displayPoints?.Select(p => new PointD(p.X / zoom, p.Y / zoom)).ToArray();
        PixelSelection selection = await Task.Run(() => CombinePixelSelection(original,
            PixelSelection.Create(document.SourceImage.Width, document.SourceImage.Height, tool, source, points), mode), token);
        if (version != _pixelSelectionRequestVersion || !ReferenceEquals(document, _currentDocument)
            || changeVersion != _documentChangeVersion || !ReferenceEquals(original, SourcePixelSelection)) return;
        ApplyPixelSelection(selection);
    }

    public async Task SelectSimilarPixelsAsync(PointD displayPoint, PixelSelectionMode mode, CancellationToken token)
    {
        if (_pixelSelectionBusy || _currentDocument is not { } document || _documentRenderer is null || _documentRasterEditor is null) return;
        int version = ++_pixelSelectionRequestVersion;
        int changeVersion = _documentChangeVersion;
        PixelSelection? original = SourcePixelSelection;
        PointD point = _canvasViewport.ToSourcePoint(displayPoint);
        point = new(Math.Clamp(point.X, 0, document.SourceImage.Width - 1), Math.Clamp(point.Y, 0, document.SourceImage.Height - 1));
        int tolerance = (int)WandTolerance;
        bool contiguous = WandContiguous;
        _pixelSelectionBusy = true;
        try
        {
            StatusText = "Selecting similar colors…";
            await _pendingDocumentSave.WaitAsync(token);
            RenderResult rendered = await _documentRenderer.RenderAsync(new(document.Id, 1, null), token);
            if (rendered.Image is null) { StatusText = $"Selection failed: {rendered.ErrorMessage}"; return; }
            RegionFillResult result = await _documentRasterEditor.FindFillRegionAsync(
                new(rendered.Image, point, tolerance) { Contiguous = contiguous }, token);
            if (result.Region is not { } region) { StatusText = $"Selection failed: {result.ErrorMessage}"; return; }
            PixelSelection selection = await Task.Run(() => CombinePixelSelection(original, PixelSelection.FromRectangles(document.SourceImage.Width, document.SourceImage.Height,
                region.Rectangles.Select(r => new RectD(region.Bounds.X + r.X * region.Bounds.Width, region.Bounds.Y + r.Y * region.Bounds.Height,
                    r.Width * region.Bounds.Width, r.Height * region.Bounds.Height))), mode), token);
            if (version != _pixelSelectionRequestVersion || !ReferenceEquals(document, _currentDocument)
                || changeVersion != _documentChangeVersion || !ReferenceEquals(original, SourcePixelSelection)) return;
            ApplyPixelSelection(selection);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StatusText = "Selection cancelled."; }
        catch (Exception exception) { StatusText = $"Selection failed: {exception.Message}"; }
        finally { _pixelSelectionBusy = false; }
    }

    private static PixelSelection CombinePixelSelection(PixelSelection? original, PixelSelection selection, PixelSelectionMode mode) =>
        (original ?? PixelSelection.Empty(selection.Width, selection.Height)).Combine(selection, mode);

    private void ApplyPixelSelection(PixelSelection selection)
    {
        _annotationSelection.SetPixelSelection(selection);
        RefreshSelection();
    }

    public void InvertPixelSelection()
    {
        if (SourcePixelSelection is not { } selection) return;
        _pixelSelectionRequestVersion++;
        _annotationSelection.SetPixelSelection(selection.Invert());
        RefreshSelection();
    }

    public async Task FillPixelSelectionAsync(ColorRgba color, CancellationToken token)
    {
        if (_currentDocument is not { } document || SourcePixelSelection is not { } selection
            || _annotationEditor is null || _documentRepository is null || color.A == 0) return;
        AnnotationObject fill = selection.CreateFill(color);
        if (!_layerWorkspace.TryAssignActiveLayer(document, fill)) { StatusText = "Show the selected layer before filling."; return; }
        await _annotationEditor.AddAsync(document, fill, token);
        await SaveCurrentDocumentAsync("Selection filled. Undo restores the previous image.", token);
    }
}
