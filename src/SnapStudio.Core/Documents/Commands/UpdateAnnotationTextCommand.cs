namespace SnapStudio.Core.Documents;

public sealed class UpdateAnnotationTextCommand : IEditCommand
{
    private readonly Guid _annotationId;
    private readonly string? _after;
    private readonly string? _before;
    private readonly string _displayName;

    public UpdateAnnotationTextCommand(
        Guid annotationId,
        string? before,
        string? after,
        string displayName = "Edit Text")
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

        FindAnnotation(document).Text = _after;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        FindAnnotation(document).Text = _before;
        return ValueTask.CompletedTask;
    }

    private AnnotationObject FindAnnotation(CaptureDocument document)
    {
        return document.Annotations.FirstOrDefault(annotation => annotation.Id == _annotationId)
            ?? throw new InvalidOperationException("The annotation could not be found.");
    }
}
