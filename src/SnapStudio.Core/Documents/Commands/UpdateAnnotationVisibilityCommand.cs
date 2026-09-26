namespace SnapStudio.Core.Documents;

public sealed class UpdateAnnotationVisibilityCommand : IEditCommand
{
    private readonly Guid _annotationId;
    private readonly bool _before;
    private readonly bool _after;

    public UpdateAnnotationVisibilityCommand(Guid annotationId, bool before, bool after)
    {
        _annotationId = annotationId;
        _before = before;
        _after = after;
    }

    public string DisplayName => _after ? "Show Object" : "Hide Object";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        SetVisibility(document, _after, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        SetVisibility(document, _before, cancellationToken);
        return ValueTask.CompletedTask;
    }

    private void SetVisibility(
        CaptureDocument document,
        bool isVisible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        AnnotationObject annotation = document.Annotations
            .FirstOrDefault(candidate => candidate.Id == _annotationId)
            ?? throw new InvalidOperationException("The annotation could not be found.");
        annotation.IsVisible = isVisible;
    }
}
