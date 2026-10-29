namespace SnapStudio.Core.Documents;

/// <summary>
/// Removes a layer and its objects as one undoable edit.
/// </summary>
public sealed class RemoveAnnotationLayerCommand(Guid layerId) : IEditCommand
{
    private List<(int Index, AnnotationLayer Layer)> _removedLayers = [];
    private List<(int Index, AnnotationObjectGroup Group)> _removedGroups = [];
    private List<(int Index, AnnotationObject Annotation)> _removedAnnotations = [];

    public string DisplayName => "Delete Layer";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        int index = document.Layers.FindIndex(layer => layer.Id == layerId);
        if (index < 0)
        {
            throw new InvalidDataException("The layer is not part of the current document.");
        }

        var ids = document.Layers.Where(layer => layer.Id == layerId || layer.FolderId == layerId).Select(layer => layer.Id).ToHashSet();
        if (!document.Layers.Any(layer => !layer.IsFolder && !ids.Contains(layer.Id)))
        {
            throw new InvalidOperationException("A document must keep at least one layer.");
        }

        _removedLayers = document.Layers.Select((layer, slot) => (Index: slot, Layer: layer)).Where(entry => ids.Contains(entry.Layer.Id)).ToList();
        _removedGroups = document.ObjectGroups.Select((group, slot) => (Index: slot, Group: group)).Where(entry => ids.Contains(entry.Group.LayerId)).ToList();
        _removedAnnotations = document.Annotations
            .Select((annotation, annotationIndex) => (Index: annotationIndex, Annotation: annotation))
            .Where(entry => entry.Annotation.LayerId is Guid id && ids.Contains(id))
            .ToList();

        document.Annotations.RemoveAll(annotation => annotation.LayerId is Guid id && ids.Contains(id));
        document.ObjectGroups.RemoveAll(group => ids.Contains(group.LayerId));
        document.Layers.RemoveAll(layer => ids.Contains(layer.Id));
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (_removedLayers.Count == 0)
        {
            throw new InvalidOperationException("The layer has not been removed.");
        }

        if (_removedLayers.Any(entry => document.Layers.Any(layer => layer.Id == entry.Layer.Id))
            || _removedGroups.Any(entry => document.ObjectGroups.Any(group => group.Id == entry.Group.Id))
            || _removedAnnotations.Any(entry => document.Annotations.Any(
                annotation => annotation.Id == entry.Annotation.Id)))
        {
            throw new InvalidDataException("The deleted layer or one of its objects already exists.");
        }

        foreach (var entry in _removedLayers) document.Layers.Insert(Math.Min(entry.Index, document.Layers.Count), entry.Layer);
        foreach (var entry in _removedGroups) document.ObjectGroups.Insert(Math.Min(entry.Index, document.ObjectGroups.Count), entry.Group);
        foreach ((int index, AnnotationObject annotation) in _removedAnnotations)
        {
            document.Annotations.Insert(Math.Min(index, document.Annotations.Count), annotation);
        }

        return ValueTask.CompletedTask;
    }
}
