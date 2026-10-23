using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ObjectActionsTests
{
    [TestMethod]
    public async Task DuplicateHasNewIdentitySameLayerAndIndependentGeometryAndImage()
    {
        CaptureDocument document = Document();
        AnnotationObject original = document.Annotations[0];
        original.Kind = AnnotationKind.Image;
        original.Image = new AnnotationImage(8, 8, [1, 2, 3]);
        original.StrokePoints = [new PointD(0, 0), new PointD(1, 1)];
        original.FillRectangles = [new RectD(0, 0, 1, 1)];
        original.Text = "Mouse pointer";
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject copy = await editor.DuplicateAsync(document, original, CancellationToken.None);
        Assert.AreNotEqual(original.Id, copy.Id);
        Assert.AreEqual(original.LayerId, copy.LayerId);
        Assert.AreEqual(original.Kind, copy.Kind);
        Assert.AreEqual(original.Text, copy.Text);
        Assert.AreEqual(original.Style, copy.Style);
        Assert.AreEqual(new RectD(30, 40, 40, 50), copy.Bounds);
        copy.StrokePoints.Clear();
        copy.FillRectangles.Clear();
        copy.Image!.PngBytes[0] = 42;
        Assert.HasCount(2, original.StrokePoints);
        Assert.HasCount(1, original.FillRectangles);
        Assert.AreEqual((byte)1, original.Image.PngBytes[0]);
        Assert.AreEqual("Duplicate Object", stack.UndoDisplayName);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSame(original, Assert.ContainsSingle(document.Annotations));
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreSame(copy, document.Annotations[1]);
        Assert.IsFalse(stack.CanRedo);
    }

    [TestMethod]
    public async Task DuplicateAtCanvasEdgeKeepsFullSizeAndOriginalUntouched()
    {
        CaptureDocument document = Document();
        AnnotationObject original = document.Annotations[0];
        original.Bounds = new RectD(180, 130, 20, 20);
        AnnotationObject copy = await new DocumentAnnotationEditor(new EditCommandStack()).DuplicateAsync(document, original, CancellationToken.None);
        Assert.AreEqual(original.Bounds, copy.Bounds);
        Assert.AreNotEqual(original.Id, copy.Id);
        Assert.AreEqual(new RectD(180, 130, 20, 20), original.Bounds);
    }

    [TestMethod]
    [DataRow(AnnotationKind.Arrow)]
    [DataRow(AnnotationKind.Line)]
    public async Task NudgePreservesDirectedLineEndpointsAndUndoesExactly(AnnotationKind kind)
    {
        CaptureDocument document = Document();
        AnnotationObject annotation = document.Annotations[0];
        annotation.Kind = kind;
        annotation.Bounds = new RectD(80, 60, -30, -40);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        Assert.IsTrue(await editor.NudgeAsync(document, annotation, 1, -10, CancellationToken.None));
        Assert.AreEqual(new RectD(81, 50, -30, -40), annotation.Bounds);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(new RectD(80, 60, -30, -40), annotation.Bounds);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual(new RectD(81, 50, -30, -40), annotation.Bounds);
    }

    [TestMethod]
    [DataRow(AnnotationKind.Image)]
    [DataRow(AnnotationKind.Fill)]
    [DataRow(AnnotationKind.Pen)]
    public async Task NudgePartiallyCroppedObjectMovesOnePixelWithoutSnappingOrStretching(AnnotationKind kind)
    {
        CaptureDocument document = Document();
        AnnotationObject annotation = document.Annotations[0];
        annotation.Kind = kind;
        annotation.Bounds = new RectD(-3, -2, 8, 8);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        Assert.IsTrue(await editor.NudgeAsync(document, annotation, 1, 0, CancellationToken.None));
        Assert.AreEqual(new RectD(-2, -2, 8, 8), annotation.Bounds);
        Assert.IsTrue(await editor.NudgeAsync(document, annotation, 0, 1, CancellationToken.None));
        Assert.AreEqual(new RectD(-2, -1, 8, 8), annotation.Bounds);
        Assert.IsFalse(await editor.NudgeAsync(document, annotation, -1, 0, CancellationToken.None));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(new RectD(-2, -2, 8, 8), annotation.Bounds);
    }

    [TestMethod]
    public async Task NudgeClampsToCanvasAndBoundaryNoOpDoesNotPolluteUndo()
    {
        CaptureDocument document = Document();
        AnnotationObject annotation = document.Annotations[0];
        annotation.Bounds = new RectD(180, 130, 20, 20);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        Assert.IsFalse(await editor.NudgeAsync(document, annotation, 10, 10, CancellationToken.None));
        Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(await editor.NudgeAsync(document, annotation, -10, -10, CancellationToken.None));
        Assert.AreEqual(new RectD(170, 120, 20, 20), annotation.Bounds);
        Assert.IsTrue(await editor.NudgeAsync(document, annotation, 100, 100, CancellationToken.None));
        Assert.AreEqual(new RectD(180, 130, 20, 20), annotation.Bounds);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ArrangeToEdgeIsOneUndoStepAndPreservesOtherLayerSlots(bool front)
    {
        CaptureDocument document = Document();
        AnnotationObject original = document.Annotations[0];
        var otherLayer = new AnnotationLayer { Name = "Other" };
        document.Layers.Add(otherLayer);
        var first = new AnnotationObject { LayerId = original.LayerId };
        var last = new AnnotationObject { LayerId = original.LayerId };
        var other1 = new AnnotationObject { LayerId = otherLayer.Id };
        var other2 = new AnnotationObject { LayerId = otherLayer.Id };
        document.Annotations = [first, other1, original, other2, last];
        Guid[] before = document.Annotations.Select(item => item.Id).ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        Assert.IsTrue(await editor.ArrangeInLayerAsync(document, original, front, CancellationToken.None));
        Assert.AreSame(original, document.Annotations[front ? 4 : 0]);
        Assert.AreSame(other1, document.Annotations[1]);
        Assert.AreSame(other2, document.Annotations[3]);
        Guid[] after = document.Annotations.Select(item => item.Id).ToArray();
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(before, document.Annotations.Select(item => item.Id));
        Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(after, document.Annotations.Select(item => item.Id));
        Assert.IsFalse(await editor.ArrangeInLayerAsync(document, original, front, CancellationToken.None));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(before, document.Annotations.Select(item => item.Id));
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task CancelledOrForeignObjectActionsDoNotChangeTheDocument()
    {
        CaptureDocument document = Document();
        AnnotationObject original = document.Annotations[0];
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.DuplicateAsync(document, original, cancelled.Token).AsTask());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.NudgeAsync(document, original, 1, 0, cancelled.Token).AsTask());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.ArrangeInLayerAsync(document, original, true, cancelled.Token).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.DuplicateAsync(document, new AnnotationObject(), CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.NudgeAsync(document, new AnnotationObject(), 1, 0, CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.ArrangeInLayerAsync(document, new AnnotationObject(), true, CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => editor.NudgeAsync(document, original, double.NaN, 0, CancellationToken.None).AsTask());
        Assert.AreSame(original, Assert.ContainsSingle(document.Annotations));
        Assert.AreEqual(new RectD(20, 30, 40, 50), original.Bounds);
        Assert.IsFalse(stack.CanUndo);
    }

    private static CaptureDocument Document()
    {
        var document = new CaptureDocument { SourceImage = new ImageAsset("source.png", 200, 150, ImagePixelFormat.Bgra32) };
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle, LayerId = document.Layers[0].Id, Bounds = new RectD(20, 30, 40, 50) });
        return document;
    }
}
