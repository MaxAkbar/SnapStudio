using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class AnnotationSelectionStateTests
{
    [TestMethod]
    public void Select_WhenAnnotationExists_TracksSelection()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();

        bool selected = selection.Select(document, document.Annotations[1].Id);

        Assert.IsTrue(selected);
        Assert.AreEqual(document.Annotations[1].Id, selection.SelectedAnnotationId);
    }

    [TestMethod]
    public void Select_WhenAnnotationIsMissing_ClearsSelection()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(1);
        var selection = new AnnotationSelectionState();
        selection.Select(document, document.Annotations[0].Id);

        bool selected = selection.Select(document, Guid.NewGuid());

        Assert.IsFalse(selected);
        Assert.IsFalse(selection.HasSelection);
    }

    [TestMethod]
    public void SelectNext_WrapsThroughAnnotations()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();

        selection.SelectNext(document);
        selection.SelectNext(document);
        selection.SelectNext(document);

        Assert.AreEqual(document.Annotations[0].Id, selection.SelectedAnnotationId);
    }

    [TestMethod]
    public void RetainExisting_ClearsRemovedAnnotation()
    {
        CaptureDocument document = CreateDocumentWithAnnotations(1);
        var selection = new AnnotationSelectionState();
        selection.Select(document, document.Annotations[0].Id);
        document.Annotations.Clear();

        selection.RetainExisting(document);

        Assert.IsFalse(selection.HasSelection);
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
