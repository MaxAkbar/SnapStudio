using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.UI;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private IReadOnlyDictionary<Guid, RectD> _annotationDragBounds = new Dictionary<Guid, RectD>();
    private Guid[] _marqueeOriginalSelection = [];
    private RectD? _marqueeOriginalBounds;
    private PixelSelection? _marqueeOriginalPixelMask;
    private PixelSelectionMode _pixelDragMode;
    private readonly List<PointD> _lassoSelectionPoints = [];
    private PixelSelection? _displayedPixelSelection;
    private double _displayedPixelZoom;
    private Geometry? _pixelSelectionGeometry;
    private RectD? _annotationDragMarqueeBounds;
    private AnnotationSelectionMode _marqueeSelectionMode;
    private Point _marqueeLastPointer;
    private RectD? _marqueePreviewBounds;
    private Storyboard? _marqueeAnimation;
    private bool _marqueeAnimationRunning;

    private void SelectAllObjectsKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || !TryHandleApplicationShortcut(args, ViewModel.HasCurrentDocument)) return;
        if (ViewModel.IsMarqueeToolActive) ViewModel.SelectEntireCanvas();
        else { ViewModel.UseMoveTool(); ViewModel.SelectAllAnnotations(); }
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private void ShowSelectionMarquee(Point end, VirtualKeyModifiers modifiers)
    {
        if (ViewModel.IsMarqueeToolActive && ViewModel.AreaSelectionTool == PixelSelectionTool.Lasso)
        {
            AppendLassoSelectionPoint(end);
            var figure = new PathFigure { StartPoint = new(_lassoSelectionPoints[0].X, _lassoSelectionPoints[0].Y), IsClosed = true, IsFilled = false };
            var line = new PolyLineSegment();
            foreach (PointD point in _lassoSelectionPoints.Skip(1)) line.Points.Add(new(point.X, point.Y));
            figure.Segments.Add(line);
            var geometry = new PathGeometry(); geometry.Figures.Add(figure);
            ShowSelectionGeometry(geometry);
            return;
        }
        var canvas = new SizeD(ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight);
        if (IsObjectModifierDown(VirtualKey.Space) && _marqueePreviewBounds is RectD previous)
        {
            RectD moved = MarqueeBoundsEditor.Move(previous, end.X - _marqueeLastPointer.X, end.Y - _marqueeLastPointer.Y, canvas);
            _annotationDragStart = new(_annotationDragStart.X + moved.X - previous.X, _annotationDragStart.Y + moved.Y - previous.Y);
            _marqueePreviewBounds = moved;
        }
        else _marqueePreviewBounds = MarqueeBoundsEditor.Create(new(_annotationDragStart.X, _annotationDragStart.Y), new(end.X, end.Y), canvas,
            square: ViewModel.IsMarqueeToolActive && _marqueeSelectionMode == AnnotationSelectionMode.Replace && modifiers.HasFlag(VirtualKeyModifiers.Shift),
            fromCenter: ViewModel.IsMarqueeToolActive && modifiers.HasFlag(VirtualKeyModifiers.Menu));
        _marqueeLastPointer = end;
        RectD preview = _marqueePreviewBounds.Value;
        if (ViewModel.IsMarqueeToolActive)
        {
            _marqueePreviewBounds = preview;
            if (ViewModel.AreaSelectionTool == PixelSelectionTool.Ellipse)
            {
                ShowSelectionGeometry(new EllipseGeometry { Center = new(preview.X + preview.Width / 2, preview.Y + preview.Height / 2), RadiusX = preview.Width / 2, RadiusY = preview.Height / 2 });
                return;
            }
        }
        ShowSelectionMarqueeBounds(preview);
    }

    private void AppendLassoSelectionPoint(Point point)
    {
        if (_lassoSelectionPoints.Count == 0 || Math.Abs(point.X - _lassoSelectionPoints[^1].X) + Math.Abs(point.Y - _lassoSelectionPoints[^1].Y) >= 1)
            _lassoSelectionPoints.Add(new(point.X, point.Y));
    }

    private void ShowMovingMarquee(Point end)
    {
        if (_marqueeOriginalBounds is not RectD source) return;
        if (_marqueeOriginalPixelMask is { } selection)
        {
            ShowPixelSelection(selection);
            RectD moved = MarqueeBoundsEditor.Move(source, (end.X - _annotationDragStart.X) / ViewModel.CanvasZoom,
                (end.Y - _annotationDragStart.Y) / ViewModel.CanvasZoom, new(selection.Width, selection.Height));
            double x = Math.Round(moved.X - source.X) * ViewModel.CanvasZoom, y = Math.Round(moved.Y - source.Y) * ViewModel.CanvasZoom;
            SelectionMarquee.RenderTransform = new TranslateTransform { X = x, Y = y };
            SelectionMarqueeUnderlay.RenderTransform = new TranslateTransform { X = x, Y = y };
            return;
        }
        RectD original = new(source.X * ViewModel.CanvasZoom, source.Y * ViewModel.CanvasZoom, source.Width * ViewModel.CanvasZoom, source.Height * ViewModel.CanvasZoom);
        ShowSelectionMarqueeBounds(MarqueeBoundsEditor.Move(original, end.X - _annotationDragStart.X, end.Y - _annotationDragStart.Y,
            new(ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight)));
    }

    private void ShowSelectionMarqueeBounds(RectD bounds)
    {
        ShowSelectionGeometry(new RectangleGeometry { Rect = new(bounds.X, bounds.Y, bounds.Width, bounds.Height) });
    }

    private void ShowPixelSelection(PixelSelection selection)
    {
        if (!ReferenceEquals(selection, _displayedPixelSelection) || _displayedPixelZoom != ViewModel.CanvasZoom)
        {
            var geometry = new PathGeometry();
            foreach (SelectionEdge edge in selection.GetEdges())
            {
                double zoom = ViewModel.CanvasZoom;
                var figure = new PathFigure { StartPoint = new(edge.Start.X * zoom, edge.Start.Y * zoom), IsFilled = false };
                figure.Segments.Add(new LineSegment { Point = new(edge.End.X * zoom, edge.End.Y * zoom) });
                geometry.Figures.Add(figure);
            }
            _pixelSelectionGeometry = geometry;
            _displayedPixelSelection = selection;
            _displayedPixelZoom = ViewModel.CanvasZoom;
        }
        ShowSelectionGeometry(_pixelSelectionGeometry!);
    }

    private void ShowSelectionGeometry(Geometry geometry)
    {
        if (!ReferenceEquals(SelectionMarquee.Data, geometry))
        {
            SelectionMarquee.Data = geometry;
            // WinUI geometry objects belong to one Path; the contrasting outline needs its own copy.
            SelectionMarqueeUnderlay.Data = CloneSelectionGeometry(geometry);
        }
        SelectionMarquee.RenderTransform = SelectionMarqueeUnderlay.RenderTransform = null;
        SelectionMarquee.Visibility = SelectionMarqueeUnderlay.Visibility = Visibility.Visible;
        if (!_marqueeAnimationRunning && IsLoaded)
        {
            if (_marqueeAnimation is null)
            {
                var offset = new DoubleAnimation { From = 0, To = -8, Duration = TimeSpan.FromMilliseconds(650), EnableDependentAnimation = true, RepeatBehavior = RepeatBehavior.Forever };
                Storyboard.SetTarget(offset, SelectionMarquee);
                Storyboard.SetTargetProperty(offset, "StrokeDashOffset");
                _marqueeAnimation = new Storyboard();
                _marqueeAnimation.Children.Add(offset);
            }
            _marqueeAnimation.Begin(); _marqueeAnimationRunning = true;
        }
    }

    private static Geometry CloneSelectionGeometry(Geometry geometry)
    {
        if (geometry is RectangleGeometry rectangle) return new RectangleGeometry { Rect = rectangle.Rect };
        if (geometry is EllipseGeometry ellipse) return new EllipseGeometry { Center = ellipse.Center, RadiusX = ellipse.RadiusX, RadiusY = ellipse.RadiusY };
        var copy = new PathGeometry();
        foreach (PathFigure figure in ((PathGeometry)geometry).Figures)
        {
            var next = new PathFigure { StartPoint = figure.StartPoint, IsClosed = figure.IsClosed, IsFilled = figure.IsFilled };
            foreach (PathSegment segment in figure.Segments)
            {
                if (segment is LineSegment line) next.Segments.Add(new LineSegment { Point = line.Point });
                else if (segment is PolyLineSegment polyline)
                {
                    var lineCopy = new PolyLineSegment();
                    foreach (Point point in polyline.Points) lineCopy.Points.Add(point);
                    next.Segments.Add(lineCopy);
                }
            }
            copy.Figures.Add(next);
        }
        return copy;
    }

    private void StopMarqueeAnimation()
    {
        _marqueeAnimation?.Stop(); _marqueeAnimationRunning = false;
    }

    private void RefreshPersistentSelectionMarquee()
    {
        if (_canvasDragMode != CanvasDragMode.None) return;
        if (ViewModel.SourcePixelSelection is { } selection) ShowPixelSelection(selection);
        else if (ViewModel.SelectionDisplayBounds is { Width: > 0, Height: > 0 } bounds) ShowSelectionMarqueeBounds(bounds);
        else
        {
            StopMarqueeAnimation(); SelectionMarquee.Visibility = SelectionMarqueeUnderlay.Visibility = Visibility.Collapsed;
            _displayedPixelSelection = null; _pixelSelectionGeometry = null;
        }
    }

    private void ShowMovedSelectionMarquee(IReadOnlyDictionary<Guid, RectD> movedBounds)
    {
        if (_annotationDragMarqueeBounds is not RectD marquee || movedBounds.Count == 0 || _canvasDragMode != CanvasDragMode.MoveAnnotation) return;
        var pair = movedBounds.First();
        RectD original = _annotationDragBounds[pair.Key];
        ShowSelectionMarqueeBounds(new RectD((marquee.X + pair.Value.X - original.X) * ViewModel.CanvasZoom,
            (marquee.Y + pair.Value.Y - original.Y) * ViewModel.CanvasZoom, marquee.Width * ViewModel.CanvasZoom, marquee.Height * ViewModel.CanvasZoom));
    }

    private IReadOnlyDictionary<Guid, RectD> CreateSelectionDragBounds(Point current, bool constrain, bool bypassSnapping)
    {
        IReadOnlyDictionary<Guid, RectD> proposed;
        if (_canvasDragMode == CanvasDragMode.MoveAnnotation && _annotationDragBounds.Count > 0)
        {
            PointD? source = ViewModel.CreateSourcePointFromCanvasPoint(new PointD(current.X, current.Y));
            proposed = source is PointD point ? ViewModel.CreateSelectionMoveBounds(_annotationDragBounds, _annotationDragStartSourcePoint, point, constrain) : _annotationDragBounds;
        }
        else proposed = CreateAnnotationDragBounds(current, constrain) is RectD bounds ? new Dictionary<Guid, RectD> { [_annotationDragId] = bounds } : new Dictionary<Guid, RectD>();
        _objectSnapGuides = [];
        if (proposed.Count == 0) return proposed;
        bool moving = _canvasDragMode == CanvasDragMode.MoveAnnotation;
        RectD group = moving ? AnnotationBoundsEditor.CombinedBounds(proposed.Values) : proposed.First().Value;
        bool horizontal = Math.Abs(current.X - _annotationDragStart.X) >= Math.Abs(current.Y - _annotationDragStart.Y);
        AnnotationSnapResult snapped = ViewModel.SnapObjectDragBounds(_annotationDragId, group, _annotationDragOriginalSourceBounds,
            _annotationDragHandle, _objectSnapTargets, constrain, !constrain || !moving || horizontal, !constrain || !moving || !horizontal, bypassSnapping);
        _objectSnapGuides = snapped.Guides;
        return moving ? proposed.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            X = pair.Value.X + snapped.Bounds.X - group.X,
            Y = pair.Value.Y + snapped.Bounds.Y - group.Y
        }) : new Dictionary<Guid, RectD> { [_annotationDragId] = snapped.Bounds };
    }

    private static Microsoft.UI.Xaml.Shapes.Rectangle CreateSelectionOutline(AnnotationOverlayItem item) => new()
    {
        Width = item.Width,
        Height = item.Height,
        Stroke = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255)),
        StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 4, 2 },
        IsHitTestVisible = false
    };
}
