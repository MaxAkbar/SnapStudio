using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public enum AnnotationSelectionMode { Replace, Add, Toggle }

public sealed class AnnotationSelectionState
{
    private readonly HashSet<Guid> _selectedIds = [];
    private RectD? _marqueeBounds;
    private RectD? _marqueeObjectBounds;

    public Guid? SelectedAnnotationId { get; private set; }

    public IReadOnlySet<Guid> SelectedAnnotationIds => _selectedIds;

    public bool HasSelection => _selectedIds.Count > 0;

    public bool HasMarquee => _marqueeBounds.HasValue;

    public bool Select(CaptureDocument document, Guid annotationId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Annotations.All(annotation => annotation.Id != annotationId))
        {
            Clear();
            return false;
        }

        Clear();
        _selectedIds.Add(annotationId);
        SelectedAnnotationId = annotationId;
        return true;
    }

    public void SelectMany(CaptureDocument document, IEnumerable<Guid> annotationIds, AnnotationSelectionMode mode = AnnotationSelectionMode.Replace)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotationIds);
        Guid[] validIds = annotationIds.Distinct().Where(id => document.Annotations.Any(annotation => annotation.Id == id)).ToArray();
        _marqueeBounds = _marqueeObjectBounds = null;
        if (mode == AnnotationSelectionMode.Replace) Clear();
        foreach (Guid id in validIds)
        {
            if (mode == AnnotationSelectionMode.Toggle && _selectedIds.Remove(id)) continue;
            _selectedIds.Add(id);
            SelectedAnnotationId = id;
        }
        RetainExisting(document);
    }

    public void SelectIntersecting(CaptureDocument document, RectD rectangle, AnnotationSelectionMode mode = AnnotationSelectionMode.Replace)
    {
        RectD area = AnnotationBoundsEditor.CombinedBounds([rectangle]);
        SelectMany(document, DocumentLayers.InPaintOrder(document)
            .Where(annotation => DocumentLayers.IsEffectivelyVisible(document, annotation))
            .Where(annotation => Intersects(area, annotation)).Select(annotation => annotation.Id), mode);
        SetMarqueeBounds(document, area);
    }

    public RectD? GetBounds(CaptureDocument document)
    {
        RectD? objectBounds = GetObjectBounds(document);
        if (_marqueeBounds is RectD marquee)
        {
            // Anchor the drawn area to the objects so move, undo, and redo keep the same crop padding.
            return objectBounds is RectD current && _marqueeObjectBounds is RectD original
                ? marquee with { X = marquee.X + current.X - original.X, Y = marquee.Y + current.Y - original.Y }
                : marquee;
        }
        return objectBounds;
    }

    public void SetMarqueeBounds(CaptureDocument document, RectD bounds)
    {
        _marqueeBounds = AnnotationBoundsEditor.CombinedBounds([bounds]);
        _marqueeObjectBounds = GetObjectBounds(document);
    }

    private RectD? GetObjectBounds(CaptureDocument document)
    {
        RectD[] bounds = document.Annotations.Where(annotation => _selectedIds.Contains(annotation.Id)).Select(annotation => annotation.Bounds).ToArray();
        return bounds.Length == 0 ? null : AnnotationBoundsEditor.CombinedBounds(bounds);
    }

    private static bool Intersects(RectD area, AnnotationObject annotation)
    {
        RectD bounds = AnnotationBoundsEditor.CombinedBounds([annotation.Bounds]);
        double radius = annotation.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Pen
            ? annotation.Style.StrokeThickness / 2 : 0;
        return bounds.Right + radius >= area.X && bounds.X - radius <= area.Right
            && bounds.Bottom + radius >= area.Y && bounds.Y - radius <= area.Bottom;
    }

    public bool SelectNext(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Annotations.Count == 0)
        {
            Clear();
            return false;
        }

        int currentIndex = SelectedAnnotationId is Guid selectedId
            ? document.Annotations.FindIndex(annotation => annotation.Id == selectedId)
            : -1;
        int nextIndex = (currentIndex + 1) % document.Annotations.Count;

        return Select(document, document.Annotations[nextIndex].Id);
    }

    public void RetainExisting(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        bool hadObjects = HasSelection;
        _selectedIds.RemoveWhere(id => document.Annotations.All(annotation => annotation.Id != id));
        if (hadObjects && !HasSelection) Clear();
        if (SelectedAnnotationId is not Guid selectedId || !_selectedIds.Contains(selectedId))
            SelectedAnnotationId = document.Annotations.LastOrDefault(annotation => _selectedIds.Contains(annotation.Id))?.Id;
    }

    public void Clear()
    {
        SelectedAnnotationId = null;
        _selectedIds.Clear();
        _marqueeBounds = _marqueeObjectBounds = null;
    }
}
