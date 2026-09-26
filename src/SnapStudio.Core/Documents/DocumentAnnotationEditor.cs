using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

/// <summary>
/// Applies annotation edits through one undoable command stack. The UI chooses tools and
/// presents status; this class owns document mutation and command construction.
/// </summary>
public sealed class DocumentAnnotationEditor(IEditCommandStack editStack)
{
    public ValueTask AddAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        if (annotation.Id == Guid.Empty)
        {
            throw new InvalidDataException("The annotation ID cannot be empty.");
        }

        if (annotation.LayerId is not Guid layerId
            || document.Layers.All(layer => layer.Id != layerId))
        {
            throw new InvalidDataException("The annotation must belong to an existing layer.");
        }

        if (document.Annotations.Any(candidate => candidate.Id == annotation.Id))
        {
            throw new InvalidDataException("The annotation ID is already in use.");
        }

        return editStack.ExecuteAsync(document, new AddAnnotationCommand(annotation), cancellationToken);
    }

    public ValueTask RemoveAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        EnsureCurrentAnnotation(document, annotation);
        return editStack.ExecuteAsync(document, new RemoveAnnotationCommand(annotation), cancellationToken);
    }

    public ValueTask SetBoundsAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        RectD bounds,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        EnsureCurrentAnnotation(document, annotation);
        return editStack.ExecuteAsync(
            document,
            new UpdateAnnotationBoundsCommand(annotation.Id, annotation.Bounds, bounds, displayName),
            cancellationToken);
    }

    public ValueTask SetTextAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        string? text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        EnsureCurrentAnnotation(document, annotation);
        return editStack.ExecuteAsync(
            document,
            new UpdateAnnotationTextCommand(annotation.Id, annotation.Text, text),
            cancellationToken);
    }

    public ValueTask SetStyleAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        AnnotationStyle style,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(style);
        EnsureCurrentAnnotation(document, annotation);
        return editStack.ExecuteAsync(
            document,
            new UpdateAnnotationStyleCommand(annotation.Id, annotation.Style, style, displayName),
            cancellationToken);
    }

    public ValueTask SetVisibilityAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        bool isVisible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        EnsureCurrentAnnotation(document, annotation);
        return editStack.ExecuteAsync(
            document,
            new UpdateAnnotationVisibilityCommand(
                annotation.Id,
                annotation.IsVisible,
                isVisible),
            cancellationToken);
    }

    private static void EnsureCurrentAnnotation(CaptureDocument document, AnnotationObject annotation)
    {
        if (!document.Annotations.Any(candidate => ReferenceEquals(candidate, annotation)))
        {
            throw new InvalidDataException("The annotation is not part of the current document.");
        }
    }
}
