namespace SnapStudio.Core.Documents;

public sealed class ArrangeAnnotationInLayerCommand(Guid annotationId, bool bringToFront) : IEditCommand
{
    private Guid? _layerId;
    private Guid[]? _before;
    private Guid[]? _after;

    public string DisplayName => bringToFront ? "Bring Object to Front" : "Send Object to Back";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        AnnotationObject annotation = document.Annotations.FirstOrDefault(item => item.Id == annotationId)
            ?? throw new InvalidOperationException("The object is no longer in this document.");
        _layerId ??= annotation.LayerId ?? throw new InvalidOperationException("The object has no layer.");
        _before ??= document.Annotations.Where(item => item.LayerId == _layerId).Select(item => item.Id).ToArray();
        Guid[] others = _before.Where(id => id != annotationId).ToArray();
        _after ??= bringToFront ? [.. others, annotationId] : [annotationId, .. others];
        Reorder(document, _before, _after);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (_before is null || _after is null) throw new InvalidOperationException("The object has not been arranged.");
        Reorder(document, _after, _before);
        return ValueTask.CompletedTask;
    }

    private void Reorder(CaptureDocument document, Guid[] expected, Guid[] order)
    {
        int[] slots = document.Annotations.Select((item, index) => (item, index))
            .Where(entry => entry.item.LayerId == _layerId).Select(entry => entry.index).ToArray();
        if (!slots.Select(index => document.Annotations[index].Id).SequenceEqual(expected))
            throw new InvalidOperationException("The object's stacking order has changed.");
        var objects = slots.Select(index => document.Annotations[index]).ToDictionary(item => item.Id);
        for (int index = 0; index < slots.Length; index++) document.Annotations[slots[index]] = objects[order[index]];
    }
}
