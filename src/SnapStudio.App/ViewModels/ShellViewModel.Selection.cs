using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private enum SelectionStyleAspect { All, Color, Width, Corners, Opacity }

    public IReadOnlySet<Guid> SelectedAnnotationIds => _annotationSelection.SelectedAnnotationIds;
    public int SelectedAnnotationCount => SelectedAnnotationIds.Count;
    public bool CanEditAnnotationText => SelectedAnnotationCount <= 1;
    public bool HasCanvasSelection => HasSelectedAnnotation || _annotationSelection.HasMarquee;

    public RectD? SourceSelectionBounds => _currentDocument is not null && _annotationSelection.GetBounds(_currentDocument) is RectD bounds
        ? SnapStudio.Core.Rendering.CanvasViewportState.Create(_currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height).ToSourceBounds(bounds)
        : null;

    public RectD? PersistentSelectionSourceBounds => _annotationSelection.HasMarquee || SelectedAnnotationCount > 1 ? SourceSelectionBounds : null;

    public RectD? SelectionDisplayBounds => PersistentSelectionSourceBounds is RectD bounds ? _canvasViewport.ToDisplayBounds(bounds) : null;

    public void RestoreAnnotationSelection(IEnumerable<Guid> ids, RectD? selectionBounds = null)
    {
        if (_currentDocument is null) return;
        _annotationSelection.SelectMany(_currentDocument, ids);
        if (selectionBounds is RectD bounds) _annotationSelection.SetMarqueeBounds(_currentDocument, bounds);
        RefreshSelection();
    }

    public void ToggleAnnotationSelection(Guid id)
    {
        if (_currentDocument is null) return;
        _annotationSelection.SelectMany(_currentDocument, [id], AnnotationSelectionMode.Toggle);
        RefreshSelection();
    }

    public void SelectAllAnnotations()
    {
        if (_currentDocument is null) return;
        _annotationSelection.SelectMany(_currentDocument, DocumentLayers.InPaintOrder(_currentDocument)
            .Where(item => DocumentLayers.IsEffectivelyVisible(_currentDocument, item)).Select(item => item.Id));
        RefreshSelection();
    }

    public void SelectAnnotationsInCanvasBounds(RectD displayBounds, AnnotationSelectionMode mode)
    {
        if (_currentDocument is null || !_canvasViewport.HasSource) return;
        RectD source = new(displayBounds.X / CanvasZoom, displayBounds.Y / CanvasZoom, displayBounds.Width / CanvasZoom, displayBounds.Height / CanvasZoom);
        _annotationSelection.SelectIntersecting(_currentDocument, source, mode);
        RefreshSelection();
    }

    public IReadOnlyDictionary<Guid, RectD> GetSelectedAnnotationBounds() => _currentDocument?.Annotations
        .Where(item => SelectedAnnotationIds.Contains(item.Id)).ToDictionary(item => item.Id, item => item.Bounds) ?? [];

    public IReadOnlyDictionary<Guid, RectD> CreateSelectionMoveBounds(IReadOnlyDictionary<Guid, RectD> original, PointD start, PointD current) =>
        _currentDocument is null ? original : AnnotationBoundsEditor.MoveSelection(original, current.X - start.X, current.Y - start.Y,
            new SizeD(_currentDocument.SourceImage.Width, _currentDocument.SourceImage.Height));

    public async Task UpdateSelectionBoundsAsync(IReadOnlyDictionary<Guid, RectD> bounds, string name, CancellationToken token)
    {
        if (bounds.Count == 1)
        {
            var pair = bounds.First();
            await UpdateAnnotationBoundsAsync(pair.Key, pair.Value, name, token);
            return;
        }
        await EditSelectionAsync((editor, document, _) => editor.SetBoundsAsync(document, bounds, name, token), $"{name} complete.", token);
    }

    public Task ArrangeSelectionAsync(int direction, bool toEdge, CancellationToken token) =>
        EditSelectionAsync((editor, document, ids) => editor.ArrangeAsync(document, ids, direction, toEdge, token), "Objects arranged.", token);

    public Task ToggleSelectedAnnotationsVisibilityAsync(CancellationToken token) =>
        EditSelectionAsync((editor, document, ids) => editor.SetVisibilityAsync(document, ids,
            !document.Annotations.Where(item => ids.Contains(item.Id)).All(item => item.IsVisible), token), "Object visibility updated.", token);

    private async Task EditSelectionAsync(Func<DocumentSelectionEditor, CaptureDocument, IReadOnlySet<Guid>, ValueTask<bool>> edit,
        string status, CancellationToken token, bool clearSelection = false)
    {
        if (_currentDocument is null || _editCommandStack is null || _documentRepository is null || !HasSelectedAnnotation) return;
        if (!await edit(new DocumentSelectionEditor(_editCommandStack), _currentDocument, SelectedAnnotationIds)) return;
        if (clearSelection) _annotationSelection.Clear();
        RefreshSelection(syncStyle: false);
        await SaveCurrentDocumentAsync(status, token);
    }

    private void RefreshSelection(bool syncStyle = true)
    {
        _layerWorkspace.SelectAnnotation(GetSelectedAnnotation());
        if (syncStyle)
        {
            if (HasSelectedAnnotation) SyncStyleControlsFromSelectedAnnotation();
            else ApplyAnnotationToolDefaultsWhenUnselected();
        }
        RefreshAnnotationOverlays();
        NotifyAnnotationStyleControlsChanged();
        NotifyEditorStateChanged();
        StatusText = SelectedAnnotationCount == 0 ? (_annotationSelection.HasMarquee ? "Area selected. Use Crop to keep this area." : "Selection cleared.")
            : $"{SelectedAnnotationCount} object{(SelectedAnnotationCount == 1 ? "" : "s")} selected.";
    }

    private Task ApplySelectionStyleAsync(SelectionStyleAspect aspect, CancellationToken token) =>
        EditSelectionAsync((editor, document, ids) => editor.SetStylesAsync(document,
            document.Annotations.Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id, item =>
            {
                AnnotationStyle before = item.Style;
                AnnotationStyle updated = CreateUpdatedStyle(item.Kind, before);
                return aspect switch
                {
                    SelectionStyleAspect.Color => before with { Stroke = updated.Stroke, Fill = updated.Fill, Text = updated.Text },
                    SelectionStyleAspect.Width => before with { StrokeThickness = updated.StrokeThickness },
                    SelectionStyleAspect.Corners => before with { CornerRadius = updated.CornerRadius },
                    SelectionStyleAspect.Opacity => before with { Opacity = updated.Opacity },
                    _ => updated
                };
            }), token), "Selected objects updated.", token);
}
