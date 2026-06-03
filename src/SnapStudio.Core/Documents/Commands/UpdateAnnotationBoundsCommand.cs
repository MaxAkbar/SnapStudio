using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public sealed class UpdateAnnotationBoundsCommand : IEditCommand
{
    private readonly Guid _annotationId;
    private readonly RectD _after;
    private readonly RectD _before;
    private readonly string _displayName;

    public UpdateAnnotationBoundsCommand(
        Guid annotationId,
        RectD before,
        RectD after,
        string displayName = "Edit Annotation")
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

        FindAnnotation(document).Bounds = _after;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        FindAnnotation(document).Bounds = _before;
        return ValueTask.CompletedTask;
    }

    private AnnotationObject FindAnnotation(CaptureDocument document)
    {
        return document.Annotations.FirstOrDefault(annotation => annotation.Id == _annotationId)
            ?? throw new InvalidOperationException("The annotation could not be found.");
    }
}
