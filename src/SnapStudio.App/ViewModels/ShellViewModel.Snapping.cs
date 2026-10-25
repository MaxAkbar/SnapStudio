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

    public AnnotationSnapResult SnapObjectDragBounds(Guid id, RectD proposed, RectD original, AnnotationBoundsHandle handle,
        IReadOnlyList<RectD> targets, bool constrain, bool allowX, bool allowY, bool bypass)
    {
        if (!ObjectSnappingEnabled || bypass || _currentDocument is null) return new(proposed, []);
        var canvas = new SizeD(_currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height);
        if (handle == AnnotationBoundsHandle.Move) return AnnotationSnapping.Move(proposed, targets, canvas, CanvasZoom, allowX, allowY, EffectiveSnapGridSpacing);
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
        return AnnotationSnapping.Resize(proposed, handle, targets, canvas, CanvasZoom, square, EffectiveSnapGridSpacing);
    }
}
