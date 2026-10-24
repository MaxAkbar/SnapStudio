using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentSelectionEditorTests
{
    [TestMethod]
    public void CropLineClippingRejectsOutsideSegmentsAndPreservesTheDirectionOfCrossingSegments()
    {
        var area = new RectD(10, 10, 100, 100);
        Assert.IsNull(AnnotationBoundsEditor.ClipLine(new RectD(20, 150, 60, -20), area));
        Assert.IsNull(AnnotationBoundsEditor.ClipLine(new RectD(0, 20, 20, -20), new RectD(15, 15, 10, 10)));
        AssertClipped(new RectD(10, 30, 100, 0), new RectD(0, 30, 150, 0));
        AssertClipped(new RectD(110, 30, -100, 0), new RectD(150, 30, -150, 0));
        AssertClipped(new RectD(30, 10, 0, 100), new RectD(30, 0, 0, 150));
        AssertClipped(new RectD(10, 20, 90, 90), new RectD(0, 10, 150, 150));

        void AssertClipped(RectD expected, RectD line)
        {
            RectD actual = AnnotationBoundsEditor.ClipLine(line, area)!.Value;
            Assert.AreEqual(expected.X, actual.X, 1e-9);
            Assert.AreEqual(expected.Y, actual.Y, 1e-9);
            Assert.AreEqual(expected.Width, actual.Width, 1e-9);
            Assert.AreEqual(expected.Height, actual.Height, 1e-9);
        }
    }

    [TestMethod]
    public async Task MoveClampsTheWholeSelectionWithoutChangingSpacingOrDirectedEndpointsAndUndoesOnce()
    {
        CaptureDocument document = Document();
        AnnotationObject[] items = document.Annotations.ToArray();
        items[1].Kind = AnnotationKind.Arrow;
        items[1].Bounds = new RectD(180, 100, -40, -20);
        RectD[] before = items.Select(item => item.Bounds).ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentSelectionEditor(stack);
        Assert.IsTrue(await editor.NudgeAsync(document, items.Take(2).Select(item => item.Id), 1000, 1000, CancellationToken.None));
        Assert.AreEqual(220d, items[0].Bounds.X - before[0].X);
        Assert.AreEqual(items[0].Bounds.X - before[0].X, items[1].Bounds.X - before[1].X);
        Assert.AreEqual(items[0].Bounds.Y - before[0].Y, items[1].Bounds.Y - before[1].Y);
        Assert.AreEqual(before[1].Width, items[1].Bounds.Width);
        Assert.AreEqual(before[2], items[2].Bounds);
        RectD[] after = items.Select(item => item.Bounds).ToArray();
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(before, items.Select(item => item.Bounds).ToArray());
        Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(after, items.Select(item => item.Bounds).ToArray());
        Assert.IsFalse(await editor.NudgeAsync(document, items.Take(2).Select(item => item.Id), 1, 1, CancellationToken.None));
    }

    [TestMethod]
    public void ClippedSelectionMovesGraduallyAndPreservesEverySize()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var original = new Dictionary<Guid, RectD> { [a] = new(-20, -10, 80, 60), [b] = new(100, 90, 30, 20) };
        var moved = AnnotationBoundsEditor.MoveSelection(original, 1, 1, new SizeD(400, 300));
        Assert.AreEqual(original[a] with { X = -19, Y = -9 }, moved[a]);
        Assert.AreEqual(original[b] with { X = 101, Y = 91 }, moved[b]);
        var blocked = AnnotationBoundsEditor.MoveSelection(original, -5, -5, new SizeD(400, 300));
        Assert.AreEqual(original[a], blocked[a]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AnnotationBoundsEditor.MoveSelection(original, double.NaN, 0, new SizeD(400, 300)));
    }

    [TestMethod]
    public async Task DeleteRestoresOriginalOrderAndReferencesInOneUndo()
    {
        CaptureDocument document = Document();
        AnnotationObject[] original = document.Annotations.ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentSelectionEditor(stack);
        await editor.RemoveAsync(document, [original[0].Id, original[2].Id], CancellationToken.None);
        Assert.AreSequenceEqual(new[] { original[1] }, document.Annotations);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(original, document.Annotations);
        Assert.IsFalse(stack.CanUndo);
        await stack.RedoAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(new[] { original[1] }, document.Annotations);
    }

    [TestMethod]
    public async Task DuplicateUsesOneOffsetAndIndependentImageBytesAndOneUndo()
    {
        CaptureDocument document = Document();
        document.Annotations[0].Kind = AnnotationKind.Image;
        document.Annotations[0].Image = new AnnotationImage(10, 10, [1, 2, 3]);
        document.Annotations[1].Bounds = new RectD(360, 270, 40, 30);
        var stack = new EditCommandStack();
        var copies = await new DocumentSelectionEditor(stack).DuplicateAsync(document, document.Annotations.Take(2).Select(item => item.Id), CancellationToken.None);
        Assert.HasCount(2, copies);
        Assert.AreEqual(document.Annotations[0].Bounds, copies[0].Bounds);
        Assert.AreEqual(document.Annotations[1].Bounds, copies[1].Bounds);
        Assert.AreNotEqual(document.Annotations[0].Id, copies[0].Id);
        copies[0].Image!.PngBytes[0] = 9;
        Assert.AreEqual((byte)1, document.Annotations[0].Image!.PngBytes[0]);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.HasCount(3, document.Annotations);
        Assert.IsFalse(stack.CanUndo);
        await stack.RedoAsync(document, CancellationToken.None);
        Assert.HasCount(5, document.Annotations);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(-1, false)]
    [DataRow(1, true)]
    [DataRow(-1, true)]
    public async Task ArrangeKeepsTheSelectedObjectsInOrderWithinEachLayer(int direction, bool toEdge)
    {
        CaptureDocument document = Document();
        var layer = new AnnotationLayer();
        document.Layers.Add(layer);
        document.Annotations.Add(new AnnotationObject { LayerId = layer.Id });
        AnnotationObject[] original = document.Annotations.ToArray();
        var stack = new EditCommandStack();
        Guid[] selected = direction > 0 ? [original[0].Id, original[1].Id] : [original[1].Id, original[2].Id];
        Assert.IsTrue(await new DocumentSelectionEditor(stack).ArrangeAsync(document, selected, direction, toEdge, CancellationToken.None));
        Guid[] order = document.Annotations.Take(3).Select(item => item.Id).ToArray();
        Assert.AreSequenceEqual(selected, order.Where(selected.Contains).ToArray());
        Assert.AreSame(original[3], document.Annotations[3]);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(original, document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task VisibilityStylesAndLayerChangesApplyToAllAndUndoTogether()
    {
        CaptureDocument document = Document();
        var layer = new AnnotationLayer();
        document.Layers.Add(layer);
        Guid[] ids = document.Annotations.Take(2).Select(item => item.Id).ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentSelectionEditor(stack);
        await editor.SetVisibilityAsync(document, ids, false, CancellationToken.None);
        Assert.IsTrue(document.Annotations.Take(2).All(item => !item.IsVisible));
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.IsTrue(document.Annotations.All(item => item.IsVisible));
        await editor.SetStylesAsync(document, document.Annotations.Take(2).ToDictionary(item => item.Id, item => item.Style with { Opacity = 0.4 }), CancellationToken.None);
        Assert.IsTrue(document.Annotations.Take(2).All(item => item.Style.Opacity == 0.4));
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.IsTrue(document.Annotations.All(item => item.Style.Opacity == 1));
        await editor.MoveToLayerAsync(document, ids, layer.Id, CancellationToken.None);
        Assert.IsTrue(document.Annotations.Where(item => ids.Contains(item.Id)).All(item => item.LayerId == layer.Id));
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.IsTrue(document.Annotations.All(item => item.LayerId == document.Layers[0].Id));
    }

    [TestMethod]
    public async Task MissingObjectsCancellationAndFailedChildDoNotLeavePartialEdits()
    {
        CaptureDocument document = Document();
        AnnotationObject[] original = document.Annotations.ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentSelectionEditor(stack);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.RemoveAsync(document, [original[0].Id, Guid.NewGuid()], CancellationToken.None).AsTask());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.RemoveAsync(document, original.Select(item => item.Id), cancelled.Token).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stack.ExecuteAsync(document, new CompositeEditCommand("Failing selection edit",
            [new RemoveAnnotationCommand(original[0]), new UpdateAnnotationBoundsCommand(Guid.NewGuid(), default, default)]), CancellationToken.None).AsTask());
        Assert.AreSequenceEqual(original, document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    private static CaptureDocument Document()
    {
        var document = new CaptureDocument { SourceImage = new ImageAsset("canvas.png", 400, 300, ImagePixelFormat.Bgra32) };
        document.Annotations = Enumerable.Range(0, 3).Select(index => new AnnotationObject
        {
            LayerId = document.Layers[0].Id,
            Kind = AnnotationKind.Rectangle,
            Bounds = new RectD(20 + 100 * index, 20, 40, 30)
        }).ToList();
        return document;
    }
}
