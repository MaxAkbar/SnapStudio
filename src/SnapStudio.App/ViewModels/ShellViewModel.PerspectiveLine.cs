using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.App.ViewModels.Items;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private bool _perspectiveLineTool;
    public bool IsPerspectiveLineToolActive => _perspectiveLineTool && !IsSelectionToolActive && !IsStampToolActive
        && ActiveToolFamily == AnnotationKind.Line && ActiveSvgToolDesign is null;

    public void UsePerspectiveLineTool()
    {
        UseDefaultToolDesign(AnnotationKind.Line);
        _perspectiveLineTool = true;
        if (CanvasGridMode == 0) CanvasGridMode = (int)SnapStudio.Core.Rendering.CanvasGridMode.FourPoint;
        ShowCanvasGrid = true;
        OnPropertyChanged(nameof(IsPerspectiveLineToolActive));
        OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
        NotifyEditorStateChanged();
        StatusText = "Perspective line: drag along a guide direction to follow its curve. Alt draws freely. Edit the result with Edit SVG.";
    }

    public PerspectiveCurveDrawingResult GetPerspectiveLineDrawing(PointD displayStart, PointD displayEnd, bool bypass,
        PerspectiveLineFamily? direction)
    {
        PointD start = _canvasViewport.ToSourcePoint(displayStart), end = _canvasViewport.ToSourcePoint(displayEnd);
        if (_currentDocument is null || !ObjectSnappingEnabled || !IsPerspectiveGridSnapEnabled || bypass)
            return new([start, end], []);
        return PerspectiveCurveDrawing.Draw(start, end, new(0, 0, _currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height),
            CanvasGridPerspective, CanvasZoom, CanvasZoom, direction, _defaultPerspectiveTargets);
    }

    private AnnotationObject? CreatePerspectiveLineAnnotation(PerspectiveCurveDrawingResult drawing)
    {
        if (drawing.Points.Count < 2 || drawing.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return null;
        double length = 0;
        for (int i = 1; i < drawing.Points.Count; i++)
            length += Math.Sqrt(Math.Pow(drawing.Points[i].X - drawing.Points[i - 1].X, 2) + Math.Pow(drawing.Points[i].Y - drawing.Points[i - 1].Y, 2));
        return length < 1 ? null : drawing.ToAnnotation(CreateAnnotationStyle(AnnotationKind.Line));
    }

    public AnnotationOverlayItem? CreatePerspectiveLinePreview(PerspectiveCurveDrawingResult drawing)
    {
        if (_currentDocument is null || !_canvasViewport.HasSource) return null;
        var annotation = CreatePerspectiveLineAnnotation(drawing);
        return annotation is null ? null : CreateLayerPreview(annotation);
    }

    public async Task AddPerspectiveLineAsync(PerspectiveCurveDrawingResult drawing, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null) return;
        var annotation = CreatePerspectiveLineAnnotation(drawing);
        if (annotation is null) { StatusText = "Drag along a perspective direction to draw a line inside the canvas and curved-grid rim."; return; }
        if (!_layerWorkspace.TryAssignActiveLayer(_currentDocument, annotation)) { StatusText = "Show the selected layer before adding an object."; return; }
        await _annotationEditor.AddAsync(_currentDocument, annotation, cancellationToken).ConfigureAwait(true);
        _annotationSelection.Select(_currentDocument, annotation.Id);
        await SaveCurrentDocumentAsync("Perspective line added.", cancellationToken).ConfigureAwait(true);
    }
}
