namespace SnapStudio.Core.Documents;

public sealed class RemoveAnnotationCommand : IEditCommand
{
    private readonly AnnotationObject _annotation;
    private int _originalIndex = -1;

    public RemoveAnnotationCommand(AnnotationObject annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        _annotation = annotation;
    }

    public string DisplayName => $"Delete {_annotation.Kind}";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        int index = document.Annotations.FindIndex(annotation => annotation.Id == _annotation.Id);
        if (index >= 0)
        {
            _originalIndex = index;
            document.Annotations.RemoveAt(index);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (document.Annotations.All(annotation => annotation.Id != _annotation.Id))
        {
            int index = _originalIndex < 0
                ? document.Annotations.Count
                : Math.Min(_originalIndex, document.Annotations.Count);
            document.Annotations.Insert(index, _annotation);
        }

        return ValueTask.CompletedTask;
    }
}
