namespace SnapStudio.Core.Documents;

public sealed class UpdateAnnotationLayerCommand(
    Guid annotationId,
    Guid beforeLayerId,
    Guid afterLayerId) : IEditCommand
{
    private int _originalIndex = -1;

    public string DisplayName => "Move Object to Layer";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        SetLayer(document, afterLayerId, moveToFront: true, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        SetLayer(document, beforeLayerId, moveToFront: false, cancellationToken);
        return ValueTask.CompletedTask;
    }

    private void SetLayer(
        CaptureDocument document,
        Guid layerId,
        bool moveToFront,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.Layers.All(layer => layer.Id != layerId))
        {
            throw new InvalidOperationException("The target layer could not be found.");
        }

        int currentIndex = document.Annotations.FindIndex(candidate => candidate.Id == annotationId);
        if (currentIndex < 0)
        {
            throw new InvalidOperationException("The annotation could not be found.");
        }

        AnnotationObject annotation = document.Annotations[currentIndex];
        if (moveToFront)
        {
            _originalIndex = currentIndex;
        }

        document.Annotations.RemoveAt(currentIndex);
        annotation.LayerId = layerId;
        int insertIndex = moveToFront
            ? document.Annotations.Count
            : Math.Min(_originalIndex, document.Annotations.Count);
        document.Annotations.Insert(insertIndex, annotation);
    }
}
