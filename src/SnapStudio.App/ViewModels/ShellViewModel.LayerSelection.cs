using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.App.ViewModels.Items;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private AnnotationLayer? ActiveSelectionLayer => _currentDocument?.Layers.FirstOrDefault(layer => layer.Id == _layerWorkspace.ActiveLayerId);
    public string SelectionLayerLabel => $"Active layer: {ActiveSelectionLayer?.Name ?? "None"}";
    public bool CanApplyLayerSelection => SourcePixelSelection is { IsEmpty: false } && ActiveSelectionLayer is { IsVisible: true, IsFolder: false };
    public bool CanRemoveLayerMask => ActiveSelectionLayer?.Mask is not null;
    public bool CanResetLayerCrop => ActiveSelectionLayer?.CropBounds is not null;

    public async Task EditLayerSelectionAsync(string action, Guid? layerId, CancellationToken token)
    {
        if (_currentDocument is not { } document || _editCommandStack is null) return;
        AnnotationLayer? layer = document.Layers.FirstOrDefault(layer => layer.Id == (layerId ?? _layerWorkspace.ActiveLayerId));
        if (layer is null || layer.IsFolder) return;
        LayerMask? mask = layer.Mask; RectD? crop = layer.CropBounds; string label;
        switch (action)
        {
            case "Mask":
                if (SourcePixelSelection is not { IsEmpty: false } selection || !layer.IsVisible) return;
                label = mask is null ? "Add Layer Mask" : "Replace Layer Mask"; mask = new(selection.GetRectangles()); break;
            case "Crop":
                if (SourcePixelSelection is not { Bounds: RectD bounds } || !layer.IsVisible) return;
                crop = layer.CropBounds is RectD previous ? LayerClipping.Intersect(previous, bounds) : bounds;
                label = "Crop Layer to Selection"; break;
            case "RemoveMask": if (mask is null) return; mask = null; label = "Remove Layer Mask"; break;
            case "ToggleMask": if (mask is null) return; mask = mask with { IsEnabled = !mask.IsEnabled }; label = mask.IsEnabled ? "Enable Layer Mask" : "Disable Layer Mask"; break;
            case "ResetCrop": if (crop is null) return; crop = null; label = "Reset Layer Crop"; break;
            default: return;
        }
        await _editCommandStack.ExecuteAsync(document, new SetLayerClippingCommand(layer.Id, mask, crop, label), token);
        await SaveCurrentDocumentAsync($"{label}: {layer.Name}. Other layers and canvas size are unchanged.", token);
    }

    private IReadOnlyList<RectD>? GetLayerClip(Guid? layerId) => _currentDocument is { } document
        && document.Layers.FirstOrDefault(layer => layer.Id == (layerId ?? _layerWorkspace.ActiveLayerId)) is { } layer
        ? LayerClipping.GetRectangles(layer, document.SourceImage.Width, document.SourceImage.Height) : null;

    private AnnotationOverlayItem CreateLayerPreview(AnnotationObject annotation) => AnnotationOverlayItem.FromAnnotation(annotation, CanvasZoom, null, layerClip: GetLayerClip(annotation.LayerId));

    public bool IsAnnotationPointVisible(Guid id, PointD point) => _currentDocument?.Annotations.FirstOrDefault(item => item.Id == id) is not { } annotation
        || ObjectClipping.Combine(GetLayerClip(annotation.LayerId), ObjectClipping.GetRectangles(annotation)) is not { } regions || regions.Any(r => point.X >= r.X && point.X < r.Right && point.Y >= r.Y && point.Y < r.Bottom);

    private void NotifyLayerSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionLayerLabel)); OnPropertyChanged(nameof(CanApplyLayerSelection));
        OnPropertyChanged(nameof(CanRemoveLayerMask)); OnPropertyChanged(nameof(CanResetLayerCrop));
    }
}
