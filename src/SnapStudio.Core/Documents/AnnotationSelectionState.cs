namespace SnapStudio.Core.Documents;

public sealed class AnnotationSelectionState
{
    public Guid? SelectedAnnotationId { get; private set; }

    public bool HasSelection => SelectedAnnotationId.HasValue;

    public bool Select(CaptureDocument document, Guid annotationId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Annotations.All(annotation => annotation.Id != annotationId))
        {
            Clear();
            return false;
        }

        SelectedAnnotationId = annotationId;
        return true;
    }

    public bool SelectNext(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Annotations.Count == 0)
        {
            Clear();
            return false;
        }

        int currentIndex = SelectedAnnotationId is Guid selectedId
            ? document.Annotations.FindIndex(annotation => annotation.Id == selectedId)
            : -1;
        int nextIndex = (currentIndex + 1) % document.Annotations.Count;

        SelectedAnnotationId = document.Annotations[nextIndex].Id;
        return true;
    }

    public void RetainExisting(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (SelectedAnnotationId is not Guid selectedId)
        {
            return;
        }

        if (document.Annotations.Any(annotation => annotation.Id == selectedId))
        {
            return;
        }

        Clear();
    }

    public void Clear()
    {
        SelectedAnnotationId = null;
    }
}
