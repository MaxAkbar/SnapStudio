using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private readonly PolygonalLasso _polygonalLasso = new();
    private DocumentId? _polygonDocumentId;
    private object? _polygonSourceImage;
    private PointD _polygonHoverPoint;
    private bool _polygonSelectionFinishing;

    private void BeginPolygonSelection(Point displayPoint)
    {
        _polygonalLasso.Clear();
        _polygonDocumentId = ViewModel.CurrentDocumentId;
        _polygonSourceImage = ViewModel.CanvasImageSource;
        _canvasDragMode = CanvasDragMode.SelectPolygon;
        if (ViewModel.CreateSourcePointFromCanvasPoint(new(displayPoint.X, displayPoint.Y)) is { } source)
        {
            _polygonalLasso.Click(source, 0);
            ShowPolygonSelectionPreview(source);
        }
    }

    private async Task AddPolygonCornerAsync(Point displayPoint)
    {
        if (ViewModel.CreateSourcePointFromCanvasPoint(new(displayPoint.X, displayPoint.Y)) is not { } source) return;
        if (_polygonalLasso.Click(source, 6 / ViewModel.CanvasZoom)) await FinishPolygonSelectionAsync();
        else ShowPolygonSelectionPreview(source);
    }

    private void ShowPolygonSelectionPreview(PointD hover)
    {
        if (_polygonalLasso.Vertices.Count == 0) return;
        _polygonHoverPoint = hover;
        double zoom = ViewModel.CanvasZoom;
        Point Display(PointD point) => new(point.X * zoom, point.Y * zoom);
        Point first = Display(_polygonalLasso.Vertices[0]);
        var outline = new PathFigure { StartPoint = first, IsFilled = false };
        var edges = new PolyLineSegment();
        foreach (PointD vertex in _polygonalLasso.Vertices.Skip(1)) edges.Points.Add(Display(vertex));
        edges.Points.Add(Display(hover));
        outline.Segments.Add(edges);
        var geometry = new PathGeometry();
        geometry.Figures.Add(outline);
        var marker = new PathFigure { StartPoint = new(first.X - 3, first.Y - 3), IsClosed = true, IsFilled = false };
        marker.Segments.Add(new PolyLineSegment { Points = [new(first.X + 3, first.Y - 3), new(first.X + 3, first.Y + 3), new(first.X - 3, first.Y + 3)] });
        geometry.Figures.Add(marker);
        ShowSelectionGeometry(geometry);
    }

    private async Task FinishPolygonSelectionAsync()
    {
        if (_canvasDragMode != CanvasDragMode.SelectPolygon || !_polygonalLasso.CanClose) return;
        PointD[] displayPoints = _polygonalLasso.Vertices.Select(point => new PointD(point.X * ViewModel.CanvasZoom, point.Y * ViewModel.CanvasZoom)).ToArray();
        PixelSelectionMode mode = _pixelDragMode;
        _polygonSelectionFinishing = true;
        try
        {
            EndCanvasDrag();
            await ViewModel.SelectPixelAreaAsync(default, displayPoints, mode, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
        finally { _polygonSelectionFinishing = false; }
    }

    private void CancelPolygonSelection()
    {
        if (_canvasDragMode != CanvasDragMode.SelectPolygon) return;
        bool sameImage = _polygonDocumentId == ViewModel.CurrentDocumentId && ReferenceEquals(_polygonSourceImage, ViewModel.CanvasImageSource);
        EndCanvasDrag();
        if (sameImage) ViewModel.RestoreAnnotationSelection(_marqueeOriginalSelection, _marqueeOriginalBounds, _marqueeOriginalPixelMask);
    }

    private async void CanvasSurface_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (_polygonSelectionFinishing) { args.Handled = true; return; }
        if (_canvasDragMode != CanvasDragMode.SelectPolygon) return;
        args.Handled = true;
        // A second click on the closing corner must not leave a new one-corner outline behind.
        if (_polygonalLasso.Vertices.Count == 1) { CancelPolygonSelection(); return; }
        await FinishPolygonSelectionAsync();
    }

    private async Task<bool> HandlePolygonSelectionKeyAsync(KeyRoutedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.SelectPolygon || ShouldIgnoreApplicationShortcut()) return false;
        if (args.Key is not (VirtualKey.Enter or VirtualKey.Escape or VirtualKey.Back or VirtualKey.Delete)) return false;
        args.Handled = true;
        if (args.Key == VirtualKey.Enter) await FinishPolygonSelectionAsync();
        else if (args.Key == VirtualKey.Escape) CancelPolygonSelection();
        else if (args.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            _polygonalLasso.RemoveLastCorner();
            if (_polygonalLasso.Vertices.Count == 0) CancelPolygonSelection();
            else ShowPolygonSelectionPreview(_polygonHoverPoint);
        }
        return true;
    }
}
