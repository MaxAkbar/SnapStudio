using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class AnnotationSelectionStateTests
{
    [Fact]
    public void Select_WhenAnnotationExists_TracksSelection()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();

        bool selected = selection.Select(document, document.Annotations[1].Id);

        Assert.True(selected);
        Assert.Equal(document.Annotations[1].Id, selection.SelectedAnnotationId);
    }

    [Fact]
    public void Select_WhenAnnotationIsMissing_ClearsSelection()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(1);
        var selection = new AnnotationSelectionState();
        selection.Select(document, document.Annotations[0].Id);

        bool selected = selection.Select(document, Guid.NewGuid());

        Assert.False(selected);
        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void SelectNext_WrapsThroughAnnotations()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();

        selection.SelectNext(document);
        selection.SelectNext(document);
        selection.SelectNext(document);

        Assert.Equal(document.Annotations[0].Id, selection.SelectedAnnotationId);
    }

    [Fact]
    public void RetainExisting_ClearsRemovedAnnotation()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(1);
        var selection = new AnnotationSelectionState();
        selection.Select(document, document.Annotations[0].Id);
        document.Annotations.Clear();

        selection.RetainExisting(document);

        Assert.False(selection.HasSelection);
    }

    private static CaptureDocument CreateDocumentWithAnnotations(int count)
    {
        var document = new CaptureDocument();
        for (int index = 0; index < count; index++)
        {
            document.Annotations.Add(new AnnotationObject
            {
                Kind = AnnotationKind.Rectangle,
                Bounds = new RectD(index * 10, index * 10, 100, 80)
            });
        }

        return document;
    }
}
