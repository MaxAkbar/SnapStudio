namespace SnapStudio.Core.Documents;

public sealed class MoveAnnotationLayerCommand(Guid layerId, int direction) : IEditCommand
{
    private int _originalIndex = -1;

    public string DisplayName => direction > 0 ? "Move Layer Up" : "Move Layer Down";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Move a layer up or down by one position.");
        }

        int index = FindIndex(document);
        int target = index + direction;
        if (target < 0 || target >= document.Layers.Count)
        {
            throw new InvalidOperationException("The layer is already at the end of the stack.");
        }

        if (_originalIndex < 0)
        {
            _originalIndex = index;
        }

        Move(document, index, target);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (_originalIndex < 0 || _originalIndex >= document.Layers.Count)
        {
            throw new InvalidOperationException("The layer has not been moved.");
        }

        Move(document, FindIndex(document), _originalIndex);
        return ValueTask.CompletedTask;
    }

    private int FindIndex(CaptureDocument document)
    {
        int index = document.Layers.FindIndex(layer => layer.Id == layerId);
        return index >= 0 ? index : throw new InvalidOperationException("The layer could not be found.");
    }

    private static void Move(CaptureDocument document, int index, int target)
    {
        AnnotationLayer layer = document.Layers[index];
        document.Layers.RemoveAt(index);
        document.Layers.Insert(target, layer);
    }
}
