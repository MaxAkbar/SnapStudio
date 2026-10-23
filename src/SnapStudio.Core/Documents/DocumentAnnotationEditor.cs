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

    public async ValueTask<bool> MoveInLayerAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        int direction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentAnnotation(document, annotation);
        if (DocumentLayers.FindAdjacentAnnotationIndex(document, annotation.Id, direction) < 0)
        {
            return false;
        }

        await editStack.ExecuteAsync(document, new MoveAnnotationInLayerCommand(annotation.Id, direction), cancellationToken)
            .ConfigureAwait(false);
        return true;
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

    public async ValueTask<AnnotationObject> DuplicateAsync(CaptureDocument document, AnnotationObject annotation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentAnnotation(document, annotation);
        var copy = new AnnotationObject
        {
            Kind = annotation.Kind,
            LayerId = annotation.LayerId,
            IsVisible = annotation.IsVisible,
            Bounds = AnnotationBoundsEditor.Move(annotation.Bounds, 10, 10,
                new SizeD(document.SourceImage.Width, document.SourceImage.Height)),
            Text = annotation.Text,
            StrokePoints = [.. annotation.StrokePoints],
            FillRectangles = [.. annotation.FillRectangles],
            Image = annotation.Image?.DeepCopy(),
            Style = annotation.Style
        };
        await editStack.ExecuteAsync(document, new AddAnnotationCommand(copy, "Duplicate Object"), cancellationToken).ConfigureAwait(false);
        return copy;
    }

    public async ValueTask<bool> NudgeAsync(CaptureDocument document, AnnotationObject annotation, double deltaX, double deltaY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentAnnotation(document, annotation);
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) throw new ArgumentOutOfRangeException(nameof(deltaX));
        RectD bounds = AnnotationBoundsEditor.Nudge(annotation.Bounds, deltaX, deltaY,
            new SizeD(document.SourceImage.Width, document.SourceImage.Height));
        if (bounds == annotation.Bounds) return false;
        await SetBoundsAsync(document, annotation, bounds, "Nudge Object", cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> ArrangeInLayerAsync(CaptureDocument document, AnnotationObject annotation, bool bringToFront, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentAnnotation(document, annotation);
        if (DocumentLayers.FindAdjacentAnnotationIndex(document, annotation.Id, bringToFront ? 1 : -1) < 0) return false;
        await editStack.ExecuteAsync(document, new ArrangeAnnotationInLayerCommand(annotation.Id, bringToFront), cancellationToken).ConfigureAwait(false);
        return true;
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

    public async ValueTask<bool> SetShapeFillAsync(
        CaptureDocument document,
        AnnotationObject annotation,
        ColorRgba color,
        double opacity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentAnnotation(document, annotation);
        if (annotation.Kind is not (AnnotationKind.Rectangle or AnnotationKind.Ellipse))
        {
            throw new ArgumentException("Only rectangles and ellipses have an editable shape fill.", nameof(annotation));
        }

        if (!double.IsFinite(opacity) || opacity < 0 || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity));
        }

        // Keep the object's overall opacity and outline. Bucket opacity affects only its fill.
        AnnotationStyle style = annotation.Style with { Fill = color with { A = (byte)Math.Round(color.A * opacity) } };
        if (style == annotation.Style)
        {
            return false;
        }

        await SetStyleAsync(document, annotation, style, $"Fill {annotation.Kind}", cancellationToken)
            .ConfigureAwait(false);
        return true;
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
