using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private bool _perspectiveRectangleTool;
    public bool IsPerspectiveRectangleToolActive => _perspectiveRectangleTool && !IsSelectionToolActive && !IsStampToolActive
        && ActiveToolFamily == AnnotationKind.Rectangle && ActiveSvgToolDesign is null;
    public PerspectiveRectanglePlane RectanglePerspectivePlane { get; private set; } = PerspectiveRectanglePlane.Side;

    public void UsePerspectiveRectangleTool(PerspectiveRectanglePlane plane)
    {
        UseDefaultToolDesign(AnnotationKind.Rectangle);
        _perspectiveRectangleTool = true;
        RectanglePerspectivePlane = plane;
        if (CanvasGridMode == 0) CanvasGridMode = (int)SnapStudio.Core.Rendering.CanvasGridMode.TwoPoint;
        ShowCanvasGrid = true;
        OnPropertyChanged(nameof(IsPerspectiveRectangleToolActive));
        OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
        NotifyEditorStateChanged();
        StatusText = "Perspective rectangle: drag opposite corners. Snap follows the selected plane; Alt draws flat. Edit the result with Edit SVG.";
    }

    private AnnotationObject? CreatePerspectiveRectangleAnnotation(PointD start, PointD end, bool bypass)
    {
        if (_currentDocument is null) return null;
        bool snap = ObjectSnappingEnabled && IsPerspectiveGridSnapEnabled && !bypass && _defaultPerspectiveTargets != PerspectiveSnapTargets.None;
        var geometry = snap ? PerspectiveRectangle.Create(start, end,
            new(0, 0, _currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height), CanvasGridPerspective, RectanglePerspectivePlane)
            : PerspectiveRectangle.Flat(start, end) is { } flat ? flat with { Plane = RectanglePerspectivePlane } : null;
        return geometry is null || geometry.Bounds.Width < 1 || geometry.Bounds.Height < 1 ? null
            : geometry.ToAnnotation(CreateAnnotationStyle(AnnotationKind.Rectangle));
    }

    public async Task AddPerspectiveRectangleAsync(PointD start, PointD end, bool bypass, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null) return;
        AnnotationObject? annotation = CreatePerspectiveRectangleAnnotation(start, end, bypass);
        if (annotation is null || annotation.Bounds.Width < 2 || annotation.Bounds.Height < 2)
        {
            StatusText = "Draw the rectangle within one perspective plane, away from its vanishing axis and curved-grid rim.";
            return;
        }
        if (!_layerWorkspace.TryAssignActiveLayer(_currentDocument, annotation)) { StatusText = "Show the selected layer before adding an object."; return; }
        await _annotationEditor.AddAsync(_currentDocument, annotation, cancellationToken).ConfigureAwait(true);
        _annotationSelection.Select(_currentDocument, annotation.Id);
        await SaveCurrentDocumentAsync("Perspective rectangle added.", cancellationToken).ConfigureAwait(true);
    }
}
