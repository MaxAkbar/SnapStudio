using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private bool _objectSnappingEnabled = true;
    public bool ObjectSnappingEnabled
    {
        get => _objectSnappingEnabled;
        set { if (_objectSnappingEnabled == value) return; _objectSnappingEnabled = value; OnPropertyChanged(nameof(ObjectSnappingEnabled)); }
    }

    public IReadOnlyList<RectD> GetObjectSnapTargets(IEnumerable<Guid> selectedIds) => _currentDocument is null ? []
        : AnnotationSnapping.GetTargets(_currentDocument, selectedIds.ToHashSet());

    public PerspectiveDrawingSnapResult SnapToolDrawing(PointD displayStart, PointD displayEnd, bool constrain, bool bypass, PerspectiveLineFamily? direction = null)
    {
        PointD start = _canvasViewport.ToSourcePoint(displayStart), end = _canvasViewport.ToSourcePoint(displayEnd);
        if (IsPerspectiveRectangleToolActive) return new(start, end, []);
        var canvas = new SizeD(_currentDocument?.SourceImage.Width ?? 0, _currentDocument?.SourceImage.Height ?? 0);
        bool enabled = ObjectSnappingEnabled && IsPerspectiveGridSnapEnabled && !bypass;
        if (enabled && ActiveAnnotationKind is AnnotationKind.Line or AnnotationKind.Arrow)
            return PerspectiveLineDrawing.Constrain(start, end, new(0, 0, canvas.Width, canvas.Height), CanvasGridPerspective, CanvasZoom, CanvasZoom, direction, _defaultPerspectiveTargets);
        return PerspectiveSnapping.Draw(start, end, ActiveAnnotationKind,
            enabled ? _defaultPerspectiveTargets.Filter(CurrentPerspectiveGrid) : [], canvas, CanvasZoom, constrain);
    }

    private IReadOnlyList<AnnotationSnapGuide> SnapNewObjectPlacement(AnnotationObject annotation, bool bypass)
    {
        if (!ObjectSnappingEnabled || !IsPerspectiveGridSnapEnabled || bypass || _currentDocument is null) return [];
        // Use final measured bounds: text fitting and stamp clamping must happen before positioning.
        var b = annotation.Bounds;
        var normalized = AnnotationBoundsEditor.CombinedBounds([b]);
        var result = PerspectiveSnapping.Move(normalized, annotation.Style.PerspectiveTargets.Filter(CurrentPerspectiveGrid),
            new(_currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height), CanvasZoom);
        annotation.Bounds = b with { X = b.X + result.Bounds.X - normalized.X, Y = b.Y + result.Bounds.Y - normalized.Y };
        return result.Guides;
    }

    public AnnotationSnapResult SnapObjectDragBounds(Guid id, RectD proposed, RectD original, AnnotationBoundsHandle handle,
        IReadOnlyList<RectD> targets, bool constrain, bool allowX, bool allowY, bool bypass)
    {
        if (!ObjectSnappingEnabled || bypass || _currentDocument is null) return new(proposed, []);
        var canvas = new SizeD(_currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height);
        var perspectiveTargets = SelectionPerspectiveTargets(id);
        double gridSpacing = perspectiveTargets.Allows(SnapStudio.Core.Rendering.GridGuideFamily.Grid) ? EffectiveSnapGridSpacing : 0;
        if (handle == AnnotationBoundsHandle.Move)
        {
            if (IsPerspectiveGridSnapEnabled && SelectedAnnotationCount == 1
                && _currentDocument.Annotations.FirstOrDefault(item => item.Id == id) is { } moved
                && perspectiveTargets != PerspectiveSnapTargets.None && PerspectiveObjectBinding.IsPerspectiveObject(moved)) return new(proposed, []);
            var aligned = AnnotationSnapping.Move(proposed, targets, canvas, CanvasZoom, allowX, allowY, gridSpacing);
            return IsPerspectiveGridSnapEnabled ? PerspectiveSnapping.Closest(proposed, aligned,
                PerspectiveSnapping.Move(proposed, perspectiveTargets.Filter(CurrentPerspectiveGrid), canvas, CanvasZoom, allowX, allowY)) : aligned;
        }
        AnnotationKind? kind = _currentDocument.Annotations.FirstOrDefault(item => item.Id == id)?.Kind;
        // Text resizing fits the installed font; its measured bounds cannot promise exact edge snapping.
        if (kind == AnnotationKind.Text) return new(proposed, []);
        bool square = constrain && kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse && handle is
            AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.TopRight or AnnotationBoundsHandle.BottomLeft or AnnotationBoundsHandle.BottomRight;
        if (square)
        {
            RectD before = AnnotationBoundsEditor.CombinedBounds([original]);
            double fixedX = handle is AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.BottomLeft ? before.Right : before.X;
            double fixedY = handle is AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.TopRight ? before.Bottom : before.Y;
            bool right = Math.Abs(proposed.X - fixedX) < 0.000001, bottom = Math.Abs(proposed.Y - fixedY) < 0.000001;
            handle = (right, bottom) switch
            {
                (true, true) => AnnotationBoundsHandle.BottomRight,
                (true, false) => AnnotationBoundsHandle.TopRight,
                (false, true) => AnnotationBoundsHandle.BottomLeft,
                _ => AnnotationBoundsHandle.TopLeft
            };
        }
        var resized = AnnotationSnapping.Resize(proposed, handle, targets, canvas, CanvasZoom, square, gridSpacing);
        return IsPerspectiveGridSnapEnabled ? PerspectiveSnapping.Closest(proposed, resized,
            PerspectiveSnapping.Resize(proposed, handle, perspectiveTargets.Filter(CurrentPerspectiveGrid), canvas, CanvasZoom, constrain)) : resized;
    }
}
