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
    private readonly Dictionary<Guid, AnnotationOverlayItem> _objects = [];

    public ObservableCollection<AnnotationOverlayItem> Overlays { get; } = [];

    public ObservableCollection<AnnotationLayerItem> Layers { get; } = [];

    public Guid? ActiveLayerId { get; private set; }

    public void OpenDocument(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ActiveLayerId = document.Layers.LastOrDefault(layer => !layer.IsFolder)?.Id;
    }

    public void ClearDocument()
    {
        ActiveLayerId = null;
        _objects.Clear();
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
        if (document?.Layers.Any(layer => layer.Id == layerId && !layer.IsFolder) != true)
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
            layer = document.Layers.Last(item => !item.IsFolder);
            ActiveLayerId = layer.Id;
        }

        if (layer.IsFolder || !LayerStructure.IsVisible(document, layer))
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
        if (!layer.IsFolder) ActiveLayerId = layerId;
        return true;
    }

    public async ValueTask<bool> MoveLayerAsync(
        CaptureDocument document, Guid layerId, int direction,
        IEditCommandStack editStack, CancellationToken cancellationToken)
    {
        var layer = document.Layers.FirstOrDefault(layer => layer.Id == layerId);
        var siblings = document.Layers.Where(item => item.FolderId == layer?.FolderId).ToArray();
        int index = Array.IndexOf(siblings, layer);
        if (index < 0 || index + direction < 0 || index + direction >= siblings.Length)
        {
            return false;
        }

        await editStack.ExecuteAsync(document, new LayerStructureCommand("Arrange Layer", planned => LayerStructureEditor.MoveLayer(planned, layerId, direction)), cancellationToken)
            .ConfigureAwait(false);
        if (!layer!.IsFolder) ActiveLayerId = layerId;
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
        if (!layer.IsFolder) ActiveLayerId = layerId;
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
        if (!layer.IsFolder) ActiveLayerId = layerId;
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
        if (index < 0 || !document.Layers.Any(layer => !layer.IsFolder && layer.Id != layerId && layer.FolderId != layerId))
        {
            return false;
        }

        await editStack.ExecuteAsync(
            document,
            new RemoveAnnotationLayerCommand(layerId),
            cancellationToken).ConfigureAwait(false);
        _collapsedLayerIds.Remove(layerId);
        if (!document.Layers.Any(layer => layer.Id == ActiveLayerId && !layer.IsFolder))
        {
            ActiveLayerId = document.Layers.Last(layer => !layer.IsFolder).Id;
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
        if (layer is null || layer.IsFolder || !LayerStructure.IsVisible(document, layer) || annotation?.LayerId is not Guid sourceLayerId
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
            ?? (document.Layers.Any(layer => layer.Id == ActiveLayerId && !layer.IsFolder)
                ? ActiveLayerId
                : document.Layers.LastOrDefault(layer => !layer.IsFolder)?.Id);
    }

    public void Refresh(
        CaptureDocument? document,
        double zoom,
        IReadOnlySet<Guid> selectedAnnotationIds,
        bool hasSource,
        bool showTransformHandles = true)
    {
        if (document is null || !hasSource)
        {
            _objects.Clear();
            Overlays.Clear();
            Layers.Clear();
            if (document is null) ActiveLayerId = null;
            return;
        }

        var itemsByLayer = new Dictionary<Guid, List<AnnotationOverlayItem>>();
        var clips = document.Layers.ToDictionary(layer => layer.Id, layer => LayerClipping.GetRectangles(layer, document.SourceImage.Width, document.SourceImage.Height));
        var overlays = new List<AnnotationOverlayItem>();
        AnnotationObject[] annotations = DocumentLayers.InPaintOrder(document).ToArray();
        var retainedIds = annotations.Select(annotation => annotation.Id).ToHashSet();
        foreach (Guid removed in _objects.Keys.Where(id => !retainedIds.Contains(id)).ToArray()) _objects.Remove(removed);
        for (int index = 0; index < annotations.Length; index++)
        {
            AnnotationObject annotation = annotations[index];
            AnnotationOverlayItem next = AnnotationOverlayItem.FromAnnotation(
                annotation,
                zoom,
                selectedAnnotationIds.Contains(annotation.Id) ? annotation.Id : null,
                index < annotations.Length - 1 && annotations[index + 1].LayerId == annotation.LayerId,
                index > 0 && annotations[index - 1].LayerId == annotation.LayerId,
                showTransformHandles, annotation.LayerId is Guid clipLayer ? clips.GetValueOrDefault(clipLayer) : null);
            if (!_objects.TryGetValue(annotation.Id, out AnnotationOverlayItem? item))
                _objects[annotation.Id] = item = next;
            else item.UpdateFrom(next);
            if (DocumentLayers.IsEffectivelyVisible(document, annotation))
            {
                overlays.Add(item);
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

        var existingLayers = Layers.ToDictionary(layer => layer.Id);
        var layers = new List<AnnotationLayerItem>();
        for (int index = document.Layers.Count - 1; index >= 0; index--)
        {
            AnnotationLayer layer = document.Layers[index];
            itemsByLayer.TryGetValue(layer.Id, out List<AnnotationOverlayItem>? items);
            if (!existingLayers.TryGetValue(layer.Id, out AnnotationLayerItem? item)) item = new AnnotationLayerItem(layer);
            item.Update(layer,
                items ?? [],
                ActiveLayerId == layer.Id,
                document.Annotations.Any(annotation => selectedAnnotationIds.Contains(annotation.Id)
                        && annotation.LayerId != layer.Id),
                !_collapsedLayerIds.Contains(layer.Id),
                document.Layers.Where(other => other.FolderId == layer.FolderId).SkipWhile(other => other.Id != layer.Id).Skip(1).Any(),
                document.Layers.Where(other => other.FolderId == layer.FolderId).TakeWhile(other => other.Id != layer.Id).Any(),
                layer.IsFolder ? document.Layers.Any(other => !other.IsFolder && other.FolderId != layer.Id) : document.Layers.Count(other => !other.IsFolder) > 1);
            layers.Add(item);
        }
        CollectionUpdates.Synchronize(Layers, layers);
        CollectionUpdates.Synchronize(Overlays, overlays);
    }
}
