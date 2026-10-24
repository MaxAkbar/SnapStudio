using System.Collections.ObjectModel;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

/// <summary>
/// Keeps layer selection and tree state separate from the shell's capture and settings workflows.
/// Document mutations still use the shared edit stack so undo and redo retain their order.
/// </summary>
internal sealed class AnnotationLayerWorkspace
{
    private readonly HashSet<Guid> _collapsedLayerIds = [];

    public ObservableCollection<AnnotationOverlayItem> Overlays { get; } = [];

    public ObservableCollection<AnnotationLayerItem> Layers { get; } = [];

    public Guid? ActiveLayerId { get; private set; }

    public void OpenDocument(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ActiveLayerId = document.Layers.LastOrDefault()?.Id;
    }

    public void ClearDocument()
    {
        ActiveLayerId = null;
        Overlays.Clear();
        Layers.Clear();
    }

    public void SelectAnnotation(AnnotationObject? annotation)
    {
        ActiveLayerId = annotation?.LayerId;
        if (ActiveLayerId is Guid layerId)
        {
            _collapsedLayerIds.Remove(layerId);
        }
    }

    public bool SelectLayer(CaptureDocument? document, Guid layerId)
    {
        if (document?.Layers.Any(layer => layer.Id == layerId) != true)
        {
            return false;
        }

        ActiveLayerId = layerId;
        return true;
    }

    public void SetExpanded(CaptureDocument? document, Guid layerId, bool isExpanded)
    {
        if (document?.Layers.Any(layer => layer.Id == layerId) != true)
        {
            return;
        }

        if (isExpanded)
        {
            _collapsedLayerIds.Remove(layerId);
        }
        else
        {
            _collapsedLayerIds.Add(layerId);
        }
    }

    public bool TryAssignActiveLayer(CaptureDocument document, AnnotationObject annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        AnnotationLayer? layer = document.Layers.FirstOrDefault(candidate => candidate.Id == ActiveLayerId);
        if (layer is null)
        {
            DocumentLayers.EnsureInitialized(document);
            layer = document.Layers[^1];
            ActiveLayerId = layer.Id;
        }

        if (!layer.IsVisible)
        {
            return false;
        }

        annotation.LayerId = layer.Id;
        _collapsedLayerIds.Remove(layer.Id);
        return true;
    }

    public async ValueTask<AnnotationLayer> AddLayerAsync(
        CaptureDocument document,
        IEditCommandStack editStack,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(editStack);

        int number = 1;
        while (document.Layers.Any(layer => layer.Name == $"Layer {number}"))
        {
            number++;
        }

        var layer = new AnnotationLayer { Name = $"Layer {number}" };
        await editStack.ExecuteAsync(document, new AddAnnotationLayerCommand(layer), cancellationToken)
            .ConfigureAwait(false);
        ActiveLayerId = layer.Id;
        _collapsedLayerIds.Remove(layer.Id);
        return layer;
    }

    public async ValueTask<bool> RenameLayerAsync(
        CaptureDocument document, Guid layerId, string name,
        IEditCommandStack editStack, CancellationToken cancellationToken)
    {
        AnnotationLayer? layer = document.Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null || layer.Name == name.Trim())
        {
            return false;
        }

        await editStack.ExecuteAsync(document, new RenameAnnotationLayerCommand(layerId, name), cancellationToken)
            .ConfigureAwait(false);
        ActiveLayerId = layerId;
        return true;
    }

    public async ValueTask<bool> MoveLayerAsync(
        CaptureDocument document, Guid layerId, int direction,
        IEditCommandStack editStack, CancellationToken cancellationToken)
    {
        int index = document.Layers.FindIndex(layer => layer.Id == layerId);
        if (index < 0 || index + direction < 0 || index + direction >= document.Layers.Count)
        {
            return false;
        }

        await editStack.ExecuteAsync(document, new MoveAnnotationLayerCommand(layerId, direction), cancellationToken)
            .ConfigureAwait(false);
        ActiveLayerId = layerId;
        return true;
    }

    public async ValueTask<bool> SetLayerColorAsync(
        CaptureDocument document, Guid layerId, LayerColorLabel color,
        IEditCommandStack editStack, CancellationToken cancellationToken)
    {
        AnnotationLayer? layer = document.Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null || (layer.ColorLabel == color && layer.CustomColor is null))
        {
            return false;
        }

        await editStack.ExecuteAsync(document, new SetLayerColorCommand(layerId, color), cancellationToken)
            .ConfigureAwait(false);
        ActiveLayerId = layerId;
        return true;
    }

    public async ValueTask<bool> SetLayerCustomColorAsync(
        CaptureDocument document, Guid layerId, ColorRgba color,
        IEditCommandStack editStack, CancellationToken cancellationToken)
    {
        AnnotationLayer? layer = document.Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        ColorRgba opaqueColor = color with { A = 255 };
        if (layer is null || (layer.ColorLabel == LayerColorLabel.None && layer.CustomColor == opaqueColor))
        {
            return false;
        }

        await editStack.ExecuteAsync(document, new SetLayerColorCommand(layerId, opaqueColor), cancellationToken)
            .ConfigureAwait(false);
        ActiveLayerId = layerId;
        return true;
    }

    public async ValueTask<bool> RemoveLayerAsync(
        CaptureDocument document,
        Guid layerId,
        IEditCommandStack editStack,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(editStack);

        int index = document.Layers.FindIndex(layer => layer.Id == layerId);
        if (index < 0 || document.Layers.Count <= 1)
        {
            return false;
        }

        await editStack.ExecuteAsync(
            document,
            new RemoveAnnotationLayerCommand(layerId),
            cancellationToken).ConfigureAwait(false);
        _collapsedLayerIds.Remove(layerId);
        if (ActiveLayerId == layerId)
        {
            ActiveLayerId = document.Layers[Math.Min(index, document.Layers.Count - 1)].Id;
        }

        return true;
    }

    public async ValueTask<AnnotationLayer?> MoveAnnotationAsync(
        CaptureDocument document,
        AnnotationObject? annotation,
        Guid targetLayerId,
        IEditCommandStack editStack,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(editStack);

        AnnotationLayer? layer = document.Layers.FirstOrDefault(candidate => candidate.Id == targetLayerId);
        if (layer is null || !layer.IsVisible || annotation?.LayerId is not Guid sourceLayerId
            || sourceLayerId == targetLayerId)
        {
            return null;
        }

        await editStack.ExecuteAsync(
            document,
            new UpdateAnnotationLayerCommand(annotation.Id, sourceLayerId, targetLayerId),
            cancellationToken).ConfigureAwait(false);
        ActiveLayerId = targetLayerId;
        _collapsedLayerIds.Remove(targetLayerId);
        return layer;
    }

    public async ValueTask<bool?> ToggleVisibilityAsync(
        CaptureDocument document,
        Guid layerId,
        IEditCommandStack editStack,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(editStack);

        AnnotationLayer? layer = document.Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null)
        {
            return null;
        }

        bool nextVisibility = !layer.IsVisible;
        await editStack.ExecuteAsync(
            document,
            new UpdateLayerVisibilityCommand(layerId, layer.IsVisible, nextVisibility),
            cancellationToken).ConfigureAwait(false);
        return nextVisibility;
    }

    public void RetainActiveLayer(CaptureDocument document, AnnotationObject? selectedAnnotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ActiveLayerId = selectedAnnotation?.LayerId
            ?? (document.Layers.Any(layer => layer.Id == ActiveLayerId)
                ? ActiveLayerId
                : document.Layers.LastOrDefault()?.Id);
    }

    public void Refresh(
        CaptureDocument? document,
        double zoom,
        IReadOnlySet<Guid> selectedAnnotationIds,
        bool hasSource)
    {
        Overlays.Clear();
        Layers.Clear();

        if (document is null || !hasSource)
        {
            return;
        }

        var itemsByLayer = new Dictionary<Guid, List<AnnotationOverlayItem>>();
        AnnotationObject[] annotations = DocumentLayers.InPaintOrder(document).ToArray();
        for (int index = 0; index < annotations.Length; index++)
        {
            AnnotationObject annotation = annotations[index];
            AnnotationOverlayItem item = AnnotationOverlayItem.FromAnnotation(
                annotation,
                zoom,
                selectedAnnotationIds.Contains(annotation.Id) ? annotation.Id : null,
                index < annotations.Length - 1 && annotations[index + 1].LayerId == annotation.LayerId,
                index > 0 && annotations[index - 1].LayerId == annotation.LayerId);
            if (DocumentLayers.IsEffectivelyVisible(document, annotation))
            {
                Overlays.Add(item);
            }

            if (annotation.LayerId is Guid layerId)
            {
                if (!itemsByLayer.TryGetValue(layerId, out List<AnnotationOverlayItem>? items))
                {
                    items = [];
                    itemsByLayer[layerId] = items;
                }

                items.Insert(0, item);
            }
        }

        for (int index = document.Layers.Count - 1; index >= 0; index--)
        {
            AnnotationLayer layer = document.Layers[index];
            itemsByLayer.TryGetValue(layer.Id, out List<AnnotationOverlayItem>? items);
            Layers.Add(new AnnotationLayerItem(
                layer,
                items ?? [],
                ActiveLayerId == layer.Id,
                document.Annotations.Any(annotation => selectedAnnotationIds.Contains(annotation.Id)
                        && annotation.LayerId != layer.Id),
                !_collapsedLayerIds.Contains(layer.Id),
                index < document.Layers.Count - 1,
                index > 0));
        }
    }
}
