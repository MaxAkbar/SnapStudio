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
                LayerId = document.Layers[0].Id,
                Kind = AnnotationKind.Rectangle,
                Bounds = new RectD(index * 10, index * 10, 100, 80)
            });
        }

        return document;
    }

    [TestMethod]
    public void MarqueeSelectsPartlyOverlappingObjectsAcrossVisibleLayersInEitherDirection()
    {
        var document = CreateDocumentWithAnnotations(3);
        document.Annotations[0].Bounds = new RectD(10, 10, 30, 30);
        document.Annotations[1].Bounds = new RectD(60, 10, 30, 30);
        document.Annotations[2].Bounds = new RectD(200, 10, 30, 30);
        var upperLayer = new AnnotationLayer();
        document.Layers.Add(upperLayer);
        document.Annotations[1].LayerId = upperLayer.Id;
        var selection = new AnnotationSelectionState();
        selection.SelectIntersecting(document, new RectD(35, 20, 30, 10));
        Assert.AreSequenceEqual(document.Annotations.Take(2).Select(item => item.Id).ToArray(), selection.SelectedAnnotationIds.ToArray());
        selection.SelectIntersecting(document, new RectD(65, 30, -30, -10));
        Assert.HasCount(2, selection.SelectedAnnotationIds);
        Assert.AreEqual(document.Annotations[1].Id, selection.SelectedAnnotationId);
    }

    [TestMethod]
    public void MarqueeIgnoresHiddenObjectsAndHiddenLayersAndIncludesThinDirectedLines()
    {
        var document = CreateDocumentWithAnnotations(4);
        document.Annotations[0].IsVisible = false;
        var hidden = new AnnotationLayer { IsVisible = false };
        document.Layers.Add(hidden);
        document.Annotations[1].LayerId = hidden.Id;
        document.Annotations[2].Kind = AnnotationKind.Line;
        document.Annotations[2].Bounds = new RectD(80, 50, -60, 0);
        document.Annotations[3].Kind = AnnotationKind.Image;
        document.Annotations[3].Bounds = new RectD(150, 40, 20, 20);
        var selection = new AnnotationSelectionState();
        selection.SelectIntersecting(document, new RectD(15, 45, 150, 10));
        Assert.IsTrue(selection.SelectedAnnotationIds.SetEquals(document.Annotations.Skip(2).Select(item => item.Id)));
        selection.SelectIntersecting(document, new RectD(500, 500, 10, 10));
        Assert.IsFalse(selection.HasSelection);
    }

    [TestMethod]
    public void AddToggleAndRetainKeepOtherSelectedObjectsAndAValidPrimary()
    {
        var document = CreateDocumentWithAnnotations(3);
        var selection = new AnnotationSelectionState();
        Guid[] ids = document.Annotations.Select(item => item.Id).ToArray();
        selection.SelectMany(document, [ids[0], ids[1], ids[0], Guid.NewGuid()]);
        selection.SelectMany(document, [ids[2]], AnnotationSelectionMode.Add);
        selection.SelectMany(document, [ids[1], ids[2]], AnnotationSelectionMode.Toggle);
        Assert.AreEqual(ids[0], selection.SelectedAnnotationId);
        Assert.AreSequenceEqual(new[] { ids[0] }, selection.SelectedAnnotationIds.ToArray());
        selection.SelectMany(document, [ids[1], ids[2]], AnnotationSelectionMode.Add);
        document.Annotations.RemoveAt(2);
        selection.RetainExisting(document);
        Assert.HasCount(2, selection.SelectedAnnotationIds);
        Assert.AreEqual(ids[1], selection.SelectedAnnotationId);
        selection.SelectNext(document);
        Assert.HasCount(1, selection.SelectedAnnotationIds);
        Assert.AreEqual(ids[0], selection.SelectedAnnotationId);
    }

    [TestMethod]
    public void MarqueeBoundsKeepTheDrawnAreaInsteadOfExpandingToTheOverlappingObjects()
    {
        var document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();
        var rectangle = new RectD(25, 20, 40, 35);
        selection.SelectIntersecting(document, rectangle);
        Assert.HasCount(2, selection.SelectedAnnotationIds);
        Assert.IsTrue(selection.HasMarquee);
        Assert.AreEqual(rectangle, selection.GetBounds(document));
        selection.SelectIntersecting(document, new RectD(65, 55, -40, -35));
        Assert.AreEqual(rectangle, selection.GetBounds(document));
        selection.Clear();
        Assert.IsFalse(selection.HasMarquee);
        Assert.IsNull(selection.GetBounds(document));
    }

    [TestMethod]
    public async Task MarqueeFollowsGroupMovesAndUndoAndRedoWithoutLosingItsPadding()
    {
        var document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();
        var rectangle = new RectD(5, 5, 140, 110);
        selection.SelectIntersecting(document, rectangle);
        var stack = new EditCommandStack();
        var editor = new DocumentSelectionEditor(stack);
        document.SourceImage = new SnapStudio.Core.Capture.ImageAsset("canvas.png", 400, 300, SnapStudio.Core.Capture.ImagePixelFormat.Bgra32);
        await editor.NudgeAsync(document, selection.SelectedAnnotationIds, 10, 20, CancellationToken.None);
        Assert.AreEqual(rectangle with { X = 15, Y = 25 }, selection.GetBounds(document));
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreEqual(rectangle, selection.GetBounds(document));
        await stack.RedoAsync(document, CancellationToken.None);
        Assert.AreEqual(rectangle with { X = 15, Y = 25 }, selection.GetBounds(document));
    }

    [TestMethod]
    public void EmptyAreaCanStaySelectedAndExplicitObjectSelectionReplacesTheMarquee()
    {
        var document = CreateDocumentWithAnnotations(2);
        var selection = new AnnotationSelectionState();
        var emptyArea = new RectD(200, 200, 30, 40);
        selection.SelectIntersecting(document, emptyArea);
        selection.RetainExisting(document);
        Assert.IsFalse(selection.HasSelection);
        Assert.IsTrue(selection.HasMarquee);
        Assert.AreEqual(emptyArea, selection.GetBounds(document));
        selection.SelectMany(document, document.Annotations.Select(item => item.Id));
        Assert.IsFalse(selection.HasMarquee);
        Assert.AreEqual(new RectD(0, 0, 110, 90), selection.GetBounds(document));
        selection.Select(document, document.Annotations[0].Id);
        Assert.AreEqual(document.Annotations[0].Bounds, selection.GetBounds(document));
    }

    [TestMethod]
    public void RemovingAllSelectedObjectsDiscardsTheirStaleMarquee()
    {
        var document = CreateDocumentWithAnnotations(1);
        var selection = new AnnotationSelectionState();
        selection.SelectIntersecting(document, new RectD(0, 0, 140, 110));
        document.Annotations.Clear();
        selection.RetainExisting(document);
        Assert.IsFalse(selection.HasSelection);
        Assert.IsFalse(selection.HasMarquee);
        Assert.IsNull(selection.GetBounds(document));
    }
}
