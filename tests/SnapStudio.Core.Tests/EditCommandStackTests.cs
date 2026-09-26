using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class EditCommandStackTests
{
    [TestMethod]
    public async Task ExecuteAsync_AppliesCommandAndMakesUndoAvailable()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var annotation = new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            Bounds = new RectD(1, 2, 30, 40)
        };

        await stack.ExecuteAsync(
            document,
            new AddAnnotationCommand(annotation),
            CancellationToken.None);

        Assert.ContainsSingle(document.Annotations);
        Assert.IsTrue(stack.CanUndo);
        Assert.IsFalse(stack.CanRedo);
        Assert.AreEqual("Add Rectangle", stack.UndoDisplayName);
    }

    [TestMethod]
    public async Task UndoRedo_RevertsAndReappliesCommand()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var annotation = new AnnotationObject
        {
            Kind = AnnotationKind.Arrow,
            Bounds = new RectD(0, 0, 10, 10)
        };

        await stack.ExecuteAsync(
            document,
            new AddAnnotationCommand(annotation),
            CancellationToken.None);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.IsTrue(undone);
        Assert.IsTrue(redone);
        Assert.ContainsSingle(document.Annotations);
        Assert.AreEqual(annotation.Id, document.Annotations[0].Id);
        Assert.IsTrue(stack.CanUndo);
        Assert.IsFalse(stack.CanRedo);
    }

    [TestMethod]
    public async Task ExecuteAsync_ClearsRedoHistory()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();

        await stack.ExecuteAsync(
            document,
            new AddAnnotationCommand(CreateAnnotation(AnnotationKind.Line)),
            CancellationToken.None);
        await stack.UndoAsync(document, CancellationToken.None);

        await stack.ExecuteAsync(
            document,
            new AddAnnotationCommand(CreateAnnotation(AnnotationKind.Text)),
            CancellationToken.None);

        Assert.IsTrue(stack.CanUndo);
        Assert.IsFalse(stack.CanRedo);
        Assert.AreEqual("Add Text", stack.UndoDisplayName);
    }

    [TestMethod]
    public async Task RemoveAnnotationCommand_UndoRedo_RemovesAndRestoresAnnotation()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        AnnotationObject annotation = CreateAnnotation(AnnotationKind.Rectangle);
        document.Annotations.Add(annotation);

        await stack.ExecuteAsync(
            document,
            new RemoveAnnotationCommand(annotation),
            CancellationToken.None);

        Assert.IsEmpty(document.Annotations);
        Assert.AreEqual("Delete Rectangle", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.IsTrue(undone);
        Assert.IsTrue(redone);
        Assert.IsEmpty(document.Annotations);
    }

    [TestMethod]
    public async Task UpdateAnnotationBoundsCommand_UndoRedo_UpdatesBounds()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        AnnotationObject annotation = CreateAnnotation(AnnotationKind.Rectangle);
        document.Annotations.Add(annotation);

        await stack.ExecuteAsync(
            document,
            new UpdateAnnotationBoundsCommand(
                annotation.Id,
                annotation.Bounds,
                new RectD(20, 30, 40, 50),
                "Move Rectangle"),
            CancellationToken.None);

        Assert.AreEqual(new RectD(20, 30, 40, 50), annotation.Bounds);
        Assert.AreEqual("Move Rectangle", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.IsTrue(undone);
        Assert.IsTrue(redone);
        Assert.AreEqual(new RectD(20, 30, 40, 50), annotation.Bounds);
    }

    [TestMethod]
    public async Task UpdateAnnotationStyleCommand_UndoRedo_UpdatesStyle()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        AnnotationObject annotation = CreateAnnotation(AnnotationKind.Rectangle);
        document.Annotations.Add(annotation);
        AnnotationStyle nextStyle = annotation.Style with
        {
            Stroke = new ColorRgba(196, 43, 28, 255),
            StrokeThickness = 8
        };

        await stack.ExecuteAsync(
            document,
            new UpdateAnnotationStyleCommand(
                annotation.Id,
                annotation.Style,
                nextStyle,
                "Style Rectangle"),
            CancellationToken.None);

        Assert.AreEqual(nextStyle, annotation.Style);
        Assert.AreEqual("Style Rectangle", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.IsTrue(undone);
        Assert.IsTrue(redone);
        Assert.AreEqual(nextStyle, annotation.Style);
    }

    [TestMethod]
    public async Task UpdateAnnotationTextCommand_UndoRedo_UpdatesText()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        AnnotationObject annotation = CreateAnnotation(AnnotationKind.Text);
        annotation.Text = "Before";
        document.Annotations.Add(annotation);

        await stack.ExecuteAsync(
            document,
            new UpdateAnnotationTextCommand(
                annotation.Id,
                annotation.Text,
                "After",
                "Edit Text"),
            CancellationToken.None);

        Assert.AreEqual("After", annotation.Text);
        Assert.AreEqual("Edit Text", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.IsTrue(undone);
        Assert.IsTrue(redone);
        Assert.AreEqual("After", annotation.Text);
    }

    [TestMethod]
    public async Task UpdateDocumentRasterCommand_UndoRedo_UpdatesSourceAndAnnotations()
    {
        var document = new CaptureDocument
        {
            SourceImage = new ImageAsset("before.png", 100, 80, ImagePixelFormat.Bgra32),
            Annotations =
            [
                CreateAnnotation(AnnotationKind.Rectangle)
            ]
        };
        var stack = new EditCommandStack();
        ImageAsset afterImage = new("after.png", 50, 40, ImagePixelFormat.Bgra32);
        AnnotationObject afterAnnotation = CreateAnnotation(AnnotationKind.Rectangle);
        afterAnnotation.Bounds = new RectD(1, 2, 3, 4);

        await stack.ExecuteAsync(
            document,
            new UpdateDocumentRasterCommand(
                document.SourceImage,
                afterImage,
                document.Annotations,
                [afterAnnotation],
                document.DestructiveOperations,
                [
                    new DestructiveEditOperation(
                        Guid.NewGuid(),
                        DestructiveOperationKind.Resize,
                        null,
                        new Dictionary<string, string>())
                ],
                "Resize Document"),
            CancellationToken.None);

        Assert.AreEqual(afterImage, document.SourceImage);
        Assert.AreEqual(new RectD(1, 2, 3, 4), document.Annotations[0].Bounds);
        Assert.ContainsSingle(document.DestructiveOperations);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.IsTrue(undone);
        Assert.IsTrue(redone);
        Assert.AreEqual(afterImage, document.SourceImage);
        Assert.AreEqual(new RectD(1, 2, 3, 4), document.Annotations[0].Bounds);
    }

    [TestMethod]
    public async Task UndoAsync_WhenCommandFails_KeepsUndoHistory()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();

        await stack.ExecuteAsync(document, new FailingRevertCommand(), CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await stack.UndoAsync(document, CancellationToken.None));

        Assert.IsTrue(stack.CanUndo);
        Assert.IsFalse(stack.CanRedo);
        Assert.AreEqual("Failing revert", stack.UndoDisplayName);
    }

    private static AnnotationObject CreateAnnotation(AnnotationKind kind) => new()
    {
        Kind = kind,
        Bounds = new RectD(0, 0, 10, 10)
    };

    private sealed class FailingRevertCommand : IEditCommand
    {
        public string DisplayName => "Failing revert";

        public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Revert failed.");
        }
    }
}
