namespace SnapStudio.Core.Documents;

public sealed class MoveAnnotationInLayerCommand(Guid annotationId, int direction) : IEditCommand
{
    private Guid? _neighborId;
    private Guid? _layerId;

    public string DisplayName => direction > 0 ? "Move Object Up" : "Move Object Down";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        int neighbor = DocumentLayers.FindAdjacentAnnotationIndex(document, annotationId, direction);
        if (neighbor < 0)
        {
            throw new InvalidOperationException("The object has no neighbor in that direction within its layer.");
        }

        AnnotationObject adjacent = document.Annotations[neighbor];
        if (_neighborId.HasValue && (_neighborId != adjacent.Id || _layerId != adjacent.LayerId))
        {
            throw new InvalidOperationException("The object's stacking order has changed.");
        }

        _neighborId = adjacent.Id;
        _layerId = adjacent.LayerId;
        Swap(document, annotationId, adjacent.Id);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (_neighborId is not Guid neighborId)
        {
            throw new InvalidOperationException("The object has not been moved.");
        }

        Swap(document, annotationId, neighborId);
        return ValueTask.CompletedTask;
    }

    private void Swap(CaptureDocument document, Guid firstId, Guid secondId)
    {
        int first = document.Annotations.FindIndex(annotation => annotation.Id == firstId);
        int second = document.Annotations.FindIndex(annotation => annotation.Id == secondId);
        if (first < 0 || second < 0 || document.Annotations[first].LayerId != _layerId
            || document.Annotations[second].LayerId != _layerId)
        {
            throw new InvalidOperationException("The objects must still belong to the same layer.");
        }

        (document.Annotations[first], document.Annotations[second]) = (document.Annotations[second], document.Annotations[first]);
    }
}
