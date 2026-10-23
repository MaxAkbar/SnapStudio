namespace SnapStudio.Core.Documents;

/// <summary>Adds the pasted image and its layer as a single undoable edit.</summary>
public sealed class PasteImageLayerCommand(AnnotationLayer layer, AnnotationObject annotation) : IEditCommand
{
    public string DisplayName => "Paste Image Layer";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(annotation);
        cancellationToken.ThrowIfCancellationRequested();
        if (annotation.Kind != AnnotationKind.Image || annotation.LayerId != layer.Id
            || layer.Id == Guid.Empty || annotation.Id == Guid.Empty
            || document.Layers.Any(candidate => candidate.Id == layer.Id)
            || document.Annotations.Any(candidate => candidate.Id == annotation.Id))
            throw new InvalidDataException("The pasted layer or image is invalid or already exists.");

        document.Layers.Add(layer);
        document.Annotations.Add(annotation);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!document.Layers.Contains(layer) || !document.Annotations.Contains(annotation)
            || document.Annotations.Any(candidate => candidate.LayerId == layer.Id && candidate != annotation))
            throw new InvalidOperationException("The pasted layer has changed and cannot be undone.");

        document.Annotations.Remove(annotation);
        document.Layers.Remove(layer);
        return ValueTask.CompletedTask;
    }
}
