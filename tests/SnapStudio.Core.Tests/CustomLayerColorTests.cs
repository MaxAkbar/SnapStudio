using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CustomLayerColorTests
{
    [TestMethod]
    public async Task CustomAndPresetColors_UndoRedoRestoreExactPreviousColor()
    {
        var document = new CaptureDocument();
        AnnotationLayer layer = document.Layers[0];
        layer.ColorLabel = LayerColorLabel.Blue;
        var custom = new ColorRgba(18, 171, 205, 255);
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, custom), CancellationToken.None);
        Assert.AreEqual(LayerColorLabel.None, layer.ColorLabel);
        Assert.AreEqual(custom, layer.CustomColor);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, LayerColorLabel.Green), CancellationToken.None);
        Assert.AreEqual(LayerColorLabel.Green, layer.ColorLabel);
        Assert.IsNull(layer.CustomColor);
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
            Assert.AreEqual(custom, layer.CustomColor);
            Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
            Assert.AreEqual(LayerColorLabel.Blue, layer.ColorLabel);
            Assert.IsNull(layer.CustomColor);
            Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
            Assert.AreEqual(custom, layer.CustomColor);
            Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
            Assert.AreEqual(LayerColorLabel.Green, layer.ColorLabel);
            Assert.IsNull(layer.CustomColor);
        }
    }

    [TestMethod]
    public async Task CustomColor_CanBeReplacedAndClearedWithIndependentUndoSteps()
    {
        var document = new CaptureDocument();
        AnnotationLayer layer = document.Layers[0];
        var first = new ColorRgba(12, 34, 56, 255);
        var second = new ColorRgba(78, 90, 123, 255);
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, first), CancellationToken.None);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, second), CancellationToken.None);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, LayerColorLabel.None), CancellationToken.None);
        Assert.IsNull(layer.CustomColor);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(second, layer.CustomColor);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(first, layer.CustomColor);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.IsNull(layer.CustomColor);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task CustomColor_IsOpaqueAndRejectsMissingLayerOrCancellationBeforeEditing()
    {
        var document = new CaptureDocument();
        AnnotationLayer layer = document.Layers[0];
        var color = new ColorRgba(10, 20, 30, 40);
        var stack = new EditCommandStack();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => stack.ExecuteAsync(document,
            new SetLayerColorCommand(layer.Id, color), cancellation.Token).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stack.ExecuteAsync(document,
            new SetLayerColorCommand(Guid.NewGuid(), color), CancellationToken.None).AsTask());
        Assert.IsFalse(stack.CanUndo);
        Assert.IsNull(layer.CustomColor);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, color), CancellationToken.None);
        Assert.AreEqual(color with { A = 255 }, layer.CustomColor);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CustomColor_SurvivesStorageDuplicationAndLayerDeletionUndo(bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        var document = new CaptureDocument { Layers = [new AnnotationLayer(), new AnnotationLayer()] };
        AnnotationLayer target = document.Layers[0];
        var color = new ColorRgba(18, 171, 205, 255);
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new SetLayerColorCommand(target.Id, color), CancellationToken.None);
        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument loaded = (await repository.GetAsync(document.Id, CancellationToken.None))!;
        Assert.AreEqual(target.Id, loaded.Layers[0].Id);
        Assert.AreEqual(color, loaded.Layers[0].CustomColor);
        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(loaded, "Copy");
        Assert.AreEqual(color, duplicate.Layers[0].CustomColor);
        duplicate.Layers[0].CustomColor = ColorRgba.Black;
        Assert.AreEqual(color, loaded.Layers[0].CustomColor);
        await stack.ExecuteAsync(document, new RemoveAnnotationLayerCommand(target.Id), CancellationToken.None);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSame(target, document.Layers[0]);
        Assert.AreEqual(color, document.Layers[0].CustomColor);
    }

    [TestMethod]
    public void ExistingLayerJson_KeepsPresetAndDefaultsToNoCustomColor()
    {
        AnnotationLayer layer = JsonSerializer.Deserialize<AnnotationLayer>(
            "{\"name\":\"Notes\",\"colorLabel\":5}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(LayerColorLabel.Blue, layer.ColorLabel);
        Assert.IsNull(layer.CustomColor);
    }

    [TestMethod]
    public async Task CustomLayerLabel_DoesNotChangeRenderedImageOrObjectStyles()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "source.png");
        using (var source = new Bitmap(8, 8, PixelFormat.Format32bppArgb)) source.Save(path, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(path, 8, 8, ImagePixelFormat.Bgra32) };
        var annotation = new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            LayerId = document.Layers[0].Id,
            Bounds = new RectD(1, 1, 6, 6),
            Style = new AnnotationStyle(ColorRgba.Transparent, new ColorRgba(0, 0, 255, 255), ColorRgba.Black, 1, 1)
        };
        document.Annotations.Add(annotation);
        AnnotationStyle before = annotation.Style;
        await new SetLayerColorCommand(document.Layers[0].Id, new ColorRgba(255, 0, 0, 255))
            .ApplyAsync(document, CancellationToken.None);
        var repository = new FileSystemDocumentRepository(workspace.Path);
        await repository.SaveAsync(document, CancellationToken.None);
        RenderResult rendered = await new SystemDrawingDocumentRenderer(repository)
            .RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(rendered.Succeeded, rendered.ErrorMessage);
        using var stream = new MemoryStream(rendered.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.AreEqual(Color.Blue.ToArgb(), image.GetPixel(4, 4).ToArgb());
        Assert.AreEqual(before, annotation.Style);
    }
}
