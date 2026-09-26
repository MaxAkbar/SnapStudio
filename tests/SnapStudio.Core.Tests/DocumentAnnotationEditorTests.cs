using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentAnnotationEditorTests
{
    [TestMethod]
    public void AddAsync_RejectsAnnotationWithoutAnExistingLayer()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        var annotation = CreateAnnotation(Guid.NewGuid());

        Assert.ThrowsExactly<InvalidDataException>(() =>
            editor.AddAsync(document, annotation, CancellationToken.None));

        Assert.IsEmpty(document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public void AddAsync_RejectsDuplicateAnnotationId()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject original = CreateAnnotation(document.Layers[0].Id);
        document.Annotations.Add(original);
        AnnotationObject duplicate = CreateAnnotation(document.Layers[0].Id);
        duplicate.Id = original.Id;

        Assert.ThrowsExactly<InvalidDataException>(() =>
            editor.AddAsync(document, duplicate, CancellationToken.None));

        Assert.ContainsSingle(document.Annotations);
        Assert.AreSame(original, document.Annotations[0]);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public void AddAsync_RejectsEmptyAnnotationIdBeforeEditing()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject annotation = CreateAnnotation(document.Layers[0].Id);
        annotation.Id = Guid.Empty;

        Assert.ThrowsExactly<InvalidDataException>(() =>
            editor.AddAsync(document, annotation, CancellationToken.None));

        Assert.IsEmpty(document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task AddAsync_UsesUndoableCommandForValidAnnotation()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject annotation = CreateAnnotation(document.Layers[0].Id);

        await editor.AddAsync(document, annotation, CancellationToken.None);

        Assert.ContainsSingle(document.Annotations);
        Assert.AreSame(annotation, document.Annotations[0]);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.IsEmpty(document.Annotations);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.ContainsSingle(document.Annotations);
        Assert.AreEqual(annotation.Id, document.Annotations[0].Id);
    }

    [TestMethod]
    public async Task SetTextAsync_UsesCurrentTextAsUndoValue()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject annotation = CreateAnnotation(document.Layers[0].Id);
        annotation.Kind = AnnotationKind.Text;
        annotation.Text = "Before";
        document.Annotations.Add(annotation);

        await editor.SetTextAsync(document, annotation, "After", CancellationToken.None);

        Assert.AreEqual("After", annotation.Text);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual("Before", annotation.Text);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual("After", annotation.Text);
    }

    [TestMethod]
    public void SetTextAsync_RejectsAnnotationFromAnotherDocument()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject annotation = CreateAnnotation(document.Layers[0].Id);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            editor.SetTextAsync(document, annotation, "Changed", CancellationToken.None));

        Assert.IsFalse(stack.CanUndo);
    }

    private static AnnotationObject CreateAnnotation(Guid layerId) => new()
    {
        Kind = AnnotationKind.Rectangle,
        LayerId = layerId,
        Bounds = new RectD(1, 2, 30, 40)
    };
}
