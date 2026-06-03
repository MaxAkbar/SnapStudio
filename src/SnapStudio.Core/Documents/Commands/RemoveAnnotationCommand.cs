namespace SnapStudio.Core.Documents;

public sealed class RemoveAnnotationCommand : IEditCommand
{
    private readonly AnnotationObject _annotation;

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

        document.Annotations.RemoveAll(annotation => annotation.Id == _annotation.Id);

        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (document.Annotations.All(annotation => annotation.Id != _annotation.Id))
        {
            document.Annotations.Add(_annotation);
        }

        return ValueTask.CompletedTask;
    }
}
