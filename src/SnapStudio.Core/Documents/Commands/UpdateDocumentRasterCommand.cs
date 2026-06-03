using SnapStudio.Core.Capture;

namespace SnapStudio.Core.Documents;

public sealed class UpdateDocumentRasterCommand : IEditCommand
{
    private readonly List<AnnotationObject> _afterAnnotations;
    private readonly List<DestructiveEditOperation> _afterOperations;
    private readonly ImageAsset _afterSourceImage;
    private readonly List<AnnotationObject> _beforeAnnotations;
    private readonly List<DestructiveEditOperation> _beforeOperations;
    private readonly ImageAsset _beforeSourceImage;
    private readonly string _displayName;

    public UpdateDocumentRasterCommand(
        ImageAsset beforeSourceImage,
        ImageAsset afterSourceImage,
        IReadOnlyCollection<AnnotationObject> beforeAnnotations,
        IReadOnlyCollection<AnnotationObject> afterAnnotations,
        IReadOnlyCollection<DestructiveEditOperation> beforeOperations,
        IReadOnlyCollection<DestructiveEditOperation> afterOperations,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(beforeAnnotations);
        ArgumentNullException.ThrowIfNull(afterAnnotations);
        ArgumentNullException.ThrowIfNull(beforeOperations);
        ArgumentNullException.ThrowIfNull(afterOperations);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _beforeSourceImage = beforeSourceImage;
        _afterSourceImage = afterSourceImage;
        _beforeAnnotations = CloneAnnotations(beforeAnnotations);
        _afterAnnotations = CloneAnnotations(afterAnnotations);
        _beforeOperations = [.. beforeOperations];
        _afterOperations = [.. afterOperations];
        _displayName = displayName;
    }

    public string DisplayName => _displayName;

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        document.SourceImage = _afterSourceImage;
        document.Annotations = CloneAnnotations(_afterAnnotations);
        document.DestructiveOperations = [.. _afterOperations];

        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        document.SourceImage = _beforeSourceImage;
        document.Annotations = CloneAnnotations(_beforeAnnotations);
        document.DestructiveOperations = [.. _beforeOperations];

        return ValueTask.CompletedTask;
    }

    private static List<AnnotationObject> CloneAnnotations(
        IReadOnlyCollection<AnnotationObject> annotations)
    {
        return annotations
            .Select(annotation => new AnnotationObject
            {
                Id = annotation.Id,
                Kind = annotation.Kind,
                Bounds = annotation.Bounds,
                Text = annotation.Text,
                Style = annotation.Style
            })
            .ToList();
    }
}
