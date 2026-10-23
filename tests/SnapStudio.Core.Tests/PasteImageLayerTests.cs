using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PasteImageLayerTests
{
    [TestMethod]
    public async Task PasteCreatesIndependentVisibleLayerAboveExistingLayersWithoutChangingTheBackground()
    {
        var document = Document(400, 300);
        document.Layers[0].IsVisible = false;
        var original = new AnnotationObject { LayerId = document.Layers[0].Id };
        document.Annotations.Add(original);
        ImageAsset source = document.SourceImage;
        var editor = new DocumentAnnotationEditor(new EditCommandStack());
        var image = new AnnotationImage(120, 60, [1, 2, 3]);

        AnnotationObject pasted = await editor.PasteImageAsLayerAsync(document, image, CancellationToken.None);
        AnnotationObject second = await editor.PasteImageAsLayerAsync(document, image, CancellationToken.None);

        Assert.AreSame(source, document.SourceImage);
        Assert.AreSame(original, document.Annotations[0]);
        Assert.HasCount(3, document.Layers);
        Assert.AreEqual("Pasted image", document.Layers[1].Name);
        Assert.AreEqual("Pasted image 2", document.Layers[2].Name);
        Assert.IsTrue(document.Layers[1].IsVisible);
        Assert.IsFalse(document.Layers[0].IsVisible);
        Assert.AreEqual(document.Layers[1].Id, pasted.LayerId);
        Assert.AreEqual(document.Layers[2].Id, second.LayerId);
        Assert.AreEqual(AnnotationKind.Image, pasted.Kind);
        Assert.AreEqual(new RectD(140, 120, 120, 60), pasted.Bounds);
        Assert.AreNotEqual(pasted.Id, second.Id);
        pasted.Image!.PngBytes[0] = 42;
        Assert.AreEqual((byte)1, image.PngBytes[0]);
        Assert.AreEqual((byte)1, second.Image!.PngBytes[0]);
    }

    [TestMethod]
    [DataRow(800, 100, 0d, 125d, 400d, 50d)]
    [DataRow(100, 600, 175d, 0d, 50d, 300d)]
    [DataRow(200, 100, 100d, 100d, 200d, 100d)]
    public async Task PasteFitsOversizedImagesAndKeepsNativeResolutionAndAspectRatio(
        int width, int height, double x, double y, double displayWidth, double displayHeight)
    {
        var document = Document(400, 300);
        AnnotationObject annotation = await new DocumentAnnotationEditor(new EditCommandStack())
            .PasteImageAsLayerAsync(document, new AnnotationImage(width, height, [1]), CancellationToken.None);

        Assert.AreEqual(new RectD(x, y, displayWidth, displayHeight), annotation.Bounds);
        Assert.AreEqual(width, annotation.Image!.Width);
        Assert.AreEqual(height, annotation.Image.Height);
    }

    [TestMethod]
    public async Task UndoRemovesBothImageAndLayerInOneStepAndRedoRestoresTheirIdentities()
    {
        var document = Document(400, 300);
        Guid originalLayer = document.Layers[0].Id;
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject pasted = await editor.PasteImageAsLayerAsync(document, new AnnotationImage(10, 10, [1]), CancellationToken.None);
        AnnotationLayer layer = document.Layers[1];
        RectD originalBounds = pasted.Bounds;
        await editor.NudgeAsync(document, pasted, 10, 1, CancellationToken.None);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(originalBounds, pasted.Bounds);
        Assert.AreEqual("Paste Image Layer", stack.UndoDisplayName);

        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.IsEmpty(document.Annotations);
        Assert.AreEqual(originalLayer, Assert.ContainsSingle(document.Layers).Id);
        Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreSame(pasted, Assert.ContainsSingle(document.Annotations));
        Assert.AreSame(layer, document.Layers[1]);
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task CancelledOrInvalidPasteLeavesDocumentAndUndoHistoryUnchanged()
    {
        var document = Document(400, 300);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.PasteImageAsLayerAsync(
            document, new AnnotationImage(10, 10, [1]), cancelled.Token).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.PasteImageAsLayerAsync(
            document, new AnnotationImage(10, 10, []), CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.PasteImageAsLayerAsync(
            document, new AnnotationImage(0, 10, [1]), CancellationToken.None).AsTask());
        Assert.ContainsSingle(document.Layers);
        Assert.IsEmpty(document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PastedLayerReopensAndExportsWithTransparencyOverTheUnchangedSource(bool database)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        using (var source = new Bitmap(64, 64, PixelFormat.Format32bppArgb)) source.Save(sourcePath, ImageFormat.Png);
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        var document = Document(64, 64);
        document.SourceImage = document.SourceImage with { Path = sourcePath };
        using var image = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        image.SetPixel(2, 2, Color.Red);
        image.SetPixel(4, 4, Color.FromArgb(96, 20, 90, 180));
        using var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        var editor = new DocumentAnnotationEditor(new EditCommandStack());
        AnnotationObject pasted = await editor.PasteImageAsLayerAsync(document, new AnnotationImage(8, 8, png.ToArray()), CancellationToken.None);
        await editor.SetBoundsAsync(document, pasted, new RectD(10, 10, 8, 8), "Move Image", CancellationToken.None);
        IDocumentRepository Repository() => database
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        await Repository().SaveAsync(document, CancellationToken.None);
        CaptureDocument reopened = (await Repository().GetAsync(document.Id, CancellationToken.None))!;
        Assert.AreEqual(pasted.Bounds, reopened.Annotations[0].Bounds);
        Assert.AreSequenceEqual(png.ToArray(), reopened.Annotations[0].Image!.PngBytes);
        Assert.AreEqual("Pasted image", reopened.Layers[1].Name);
        Assert.AreEqual(document.SourceImage, reopened.SourceImage);
        RenderResult rendered = await new SystemDrawingDocumentRenderer(Repository()).RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(rendered.Succeeded, rendered.ErrorMessage);
        using var output = new MemoryStream(rendered.Image!.Pixels);
        using var bitmap = new Bitmap(output);
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).A);
        Assert.AreEqual(Color.Red.ToArgb(), bitmap.GetPixel(12, 12).ToArgb());
        Assert.IsInRange(94, 98, (int)bitmap.GetPixel(14, 14).A);
        Assert.AreSequenceEqual(sourceBytes, File.ReadAllBytes(sourcePath));
    }

    private static CaptureDocument Document(int width, int height) => new()
    {
        SourceImage = new ImageAsset("source.png", width, height, ImagePixelFormat.Bgra32)
    };
}
