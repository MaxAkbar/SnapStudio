namespace SnapStudio.Core.Documents;

public sealed class AddAnnotationCommand : IEditCommand
{
    private readonly AnnotationObject _annotation;
    private readonly string _displayName;

    public AddAnnotationCommand(AnnotationObject annotation, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        _annotation = annotation;
        _displayName = displayName ?? $"Add {annotation.Kind}";
    }

    public string DisplayName => _displayName;

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (document.Annotations.All(annotation => annotation.Id != _annotation.Id))
        {
            document.Annotations.Add(_annotation);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        document.Annotations.RemoveAll(annotation => annotation.Id == _annotation.Id);

        return ValueTask.CompletedTask;
    }
}
