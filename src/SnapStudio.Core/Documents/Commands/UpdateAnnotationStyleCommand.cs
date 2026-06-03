namespace SnapStudio.Core.Documents;

public sealed class UpdateAnnotationStyleCommand : IEditCommand
{
    private readonly Guid _annotationId;
    private readonly AnnotationStyle _after;
    private readonly AnnotationStyle _before;
    private readonly string _displayName;

    public UpdateAnnotationStyleCommand(
        Guid annotationId,
        AnnotationStyle before,
        AnnotationStyle after,
        string displayName = "Style Annotation")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _annotationId = annotationId;
        _before = before;
        _after = after;
        _displayName = displayName;
    }

    public string DisplayName => _displayName;

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        FindAnnotation(document).Style = _after;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        FindAnnotation(document).Style = _before;
        return ValueTask.CompletedTask;
    }

    private AnnotationObject FindAnnotation(CaptureDocument document)
    {
        return document.Annotations.FirstOrDefault(annotation => annotation.Id == _annotationId)
            ?? throw new InvalidOperationException("The annotation could not be found.");
    }
}
