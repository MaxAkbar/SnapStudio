namespace SnapStudio.Core.Documents;

/// <summary>
/// Removes a layer and its objects as one undoable edit.
/// </summary>
public sealed class RemoveAnnotationLayerCommand(Guid layerId) : IEditCommand
{
    private AnnotationLayer? _removedLayer;
    private int _originalLayerIndex = -1;
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

        if (document.Layers.Count <= 1)
        {
            throw new InvalidOperationException("A document must keep at least one layer.");
        }

        _removedLayer = document.Layers[index];
        _originalLayerIndex = index;
        _removedAnnotations = document.Annotations
            .Select((annotation, annotationIndex) => (Index: annotationIndex, Annotation: annotation))
            .Where(entry => entry.Annotation.LayerId == layerId)
            .ToList();

        document.Annotations.RemoveAll(annotation => annotation.LayerId == layerId);
        document.Layers.RemoveAt(index);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (_removedLayer is null || _originalLayerIndex < 0)
        {
            throw new InvalidOperationException("The layer has not been removed.");
        }

        if (document.Layers.Any(layer => layer.Id == layerId)
            || _removedAnnotations.Any(entry => document.Annotations.Any(
                annotation => annotation.Id == entry.Annotation.Id)))
        {
            throw new InvalidDataException("The deleted layer or one of its objects already exists.");
        }

        document.Layers.Insert(Math.Min(_originalLayerIndex, document.Layers.Count), _removedLayer);
        foreach ((int index, AnnotationObject annotation) in _removedAnnotations)
        {
            document.Annotations.Insert(Math.Min(index, document.Annotations.Count), annotation);
        }

        return ValueTask.CompletedTask;
    }
}
