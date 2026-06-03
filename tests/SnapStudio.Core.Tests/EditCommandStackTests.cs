using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class EditCommandStackTests
{
    [Fact]
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

        Assert.Single(document.Annotations);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal("Add Rectangle", stack.UndoDisplayName);
    }

    [Fact]
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

        Assert.True(undone);
        Assert.True(redone);
        Assert.Single(document.Annotations);
        Assert.Equal(annotation.Id, document.Annotations[0].Id);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
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

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal("Add Text", stack.UndoDisplayName);
    }

    [Fact]
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

        Assert.Empty(document.Annotations);
        Assert.Equal("Delete Rectangle", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.True(undone);
        Assert.True(redone);
        Assert.Empty(document.Annotations);
    }

    [Fact]
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

        Assert.Equal(new RectD(20, 30, 40, 50), annotation.Bounds);
        Assert.Equal("Move Rectangle", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.True(undone);
        Assert.True(redone);
        Assert.Equal(new RectD(20, 30, 40, 50), annotation.Bounds);
    }

    [Fact]
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

        Assert.Equal(nextStyle, annotation.Style);
        Assert.Equal("Style Rectangle", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.True(undone);
        Assert.True(redone);
        Assert.Equal(nextStyle, annotation.Style);
    }

    [Fact]
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

        Assert.Equal("After", annotation.Text);
        Assert.Equal("Edit Text", stack.UndoDisplayName);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.True(undone);
        Assert.True(redone);
        Assert.Equal("After", annotation.Text);
    }

    [Fact]
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

        Assert.Equal(afterImage, document.SourceImage);
        Assert.Equal(new RectD(1, 2, 3, 4), document.Annotations[0].Bounds);
        Assert.Single(document.DestructiveOperations);

        bool undone = await stack.UndoAsync(document, CancellationToken.None);
        bool redone = await stack.RedoAsync(document, CancellationToken.None);

        Assert.True(undone);
        Assert.True(redone);
        Assert.Equal(afterImage, document.SourceImage);
        Assert.Equal(new RectD(1, 2, 3, 4), document.Annotations[0].Bounds);
    }

    [Fact]
    public async Task UndoAsync_WhenCommandFails_KeepsUndoHistory()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();

        await stack.ExecuteAsync(document, new FailingRevertCommand(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await stack.UndoAsync(document, CancellationToken.None));

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal("Failing revert", stack.UndoDisplayName);
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
