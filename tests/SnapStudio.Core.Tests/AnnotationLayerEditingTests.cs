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
public sealed class AnnotationLayerEditingTests
{
    [TestMethod]
    public async Task Rename_TrimsNameAndSupportsUndoRedoWithoutChangingIdentity()
    {
        var document = new CaptureDocument();
        AnnotationLayer layer = document.Layers[0];
        document.Annotations.Add(new AnnotationObject { LayerId = layer.Id });
        var stack = new EditCommandStack();

        await stack.ExecuteAsync(document, new RenameAnnotationLayerCommand(layer.Id, "  Callouts  "), CancellationToken.None);
        Assert.AreEqual("Callouts", layer.Name);
        Assert.AreEqual(layer.Id, document.Annotations[0].LayerId);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual("Layer 1", layer.Name);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual("Callouts", layer.Name);
    }

    [TestMethod]
    public async Task Rename_RejectsBlankOrOversizedNamesWithoutAddingUndo()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        foreach (string name in new[] { "", "   ", new string('x', RenameAnnotationLayerCommand.MaximumNameLength + 1) })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => stack.ExecuteAsync(
                document, new RenameAnnotationLayerCommand(document.Layers[0].Id, name), CancellationToken.None).AsTask());
            Assert.AreEqual("Layer 1", document.Layers[0].Name);
            Assert.IsFalse(stack.CanUndo);
        }
    }

    [TestMethod]
    public async Task Color_CanBeChangedAndClearedWithUndoRedo()
    {
        var document = new CaptureDocument();
        AnnotationLayer layer = document.Layers[0];
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, LayerColorLabel.Blue), CancellationToken.None);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(layer.Id, LayerColorLabel.None), CancellationToken.None);
        Assert.AreEqual(LayerColorLabel.None, layer.ColorLabel);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(LayerColorLabel.Blue, layer.ColorLabel);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(LayerColorLabel.None, layer.ColorLabel);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual(LayerColorLabel.Blue, layer.ColorLabel);
    }

    [TestMethod]
    public async Task Color_RejectsUnknownLabelsWithoutChangingDocument()
    {
        var document = new CaptureDocument();
        var stack = new EditCommandStack();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => stack.ExecuteAsync(
            document, new SetLayerColorCommand(document.Layers[0].Id, (LayerColorLabel)99), CancellationToken.None).AsTask());
        Assert.AreEqual(LayerColorLabel.None, document.Layers[0].ColorLabel);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public async Task Move_ChangesPaintOrderAndRestoresExactOrderOnUndo(int direction)
    {
        var bottom = new AnnotationLayer { Name = "Bottom" };
        var middle = new AnnotationLayer { Name = "Middle" };
        var top = new AnnotationLayer { Name = "Top" };
        var document = new CaptureDocument { Layers = [bottom, middle, top] };
        document.Annotations = document.Layers.Select(layer => new AnnotationObject { LayerId = layer.Id }).ToList();
        var stack = new EditCommandStack();
        Guid[] originalAnnotations = document.Annotations.Select(annotation => annotation.Id).ToArray();
        await stack.ExecuteAsync(document, new MoveAnnotationLayerCommand(middle.Id, direction), CancellationToken.None);
        Guid[] expected = direction > 0 ? [bottom.Id, top.Id, middle.Id] : [middle.Id, bottom.Id, top.Id];
        Assert.AreSequenceEqual(expected, document.Layers.Select(layer => layer.Id));
        Assert.AreSequenceEqual(expected, DocumentLayers.InPaintOrder(document).Select(annotation => annotation.LayerId!.Value));
        Assert.AreSequenceEqual(originalAnnotations, document.Annotations.Select(annotation => annotation.Id));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(new[] { bottom.Id, middle.Id, top.Id }, document.Layers.Select(layer => layer.Id));
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(expected, document.Layers.Select(layer => layer.Id));
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(0, -1)]
    public async Task Move_RejectsMovementPastStackEdges(int index, int direction)
    {
        var document = new CaptureDocument { Layers = [new AnnotationLayer(), new AnnotationLayer()] };
        Guid[] original = document.Layers.Select(layer => layer.Id).ToArray();
        var stack = new EditCommandStack();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stack.ExecuteAsync(
            document, new MoveAnnotationLayerCommand(document.Layers[index].Id, direction), CancellationToken.None).AsTask());
        Assert.AreSequenceEqual(original, document.Layers.Select(layer => layer.Id));
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task Move_RejectsInvalidDirection()
    {
        var document = new CaptureDocument();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => new MoveAnnotationLayerCommand(document.Layers[0].Id, 0)
            .ApplyAsync(document, CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task LayerChanges_WhenCancelled_LeaveDocumentAndUndoUnchanged()
    {
        var document = new CaptureDocument { Layers = [new AnnotationLayer(), new AnnotationLayer()] };
        AnnotationLayer layer = document.Layers[0];
        var stack = new EditCommandStack();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IEditCommand[] commands = [new RenameAnnotationLayerCommand(layer.Id, "Changed"),
            new MoveAnnotationLayerCommand(layer.Id, 1), new SetLayerColorCommand(layer.Id, LayerColorLabel.Green)];
        foreach (IEditCommand command in commands)
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                stack.ExecuteAsync(document, command, cancellation.Token).AsTask());
        }

        Assert.AreSame(layer, document.Layers[0]);
        Assert.AreEqual("Layer 1", layer.Name);
        Assert.AreEqual(LayerColorLabel.None, layer.ColorLabel);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task LayerChanges_RejectMissingLayers()
    {
        var document = new CaptureDocument();
        Guid missing = Guid.NewGuid();
        IEditCommand[] commands = [new RenameAnnotationLayerCommand(missing, "Changed"),
            new MoveAnnotationLayerCommand(missing, 1), new SetLayerColorCommand(missing, LayerColorLabel.Green)];
        foreach (IEditCommand command in commands)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ApplyAsync(document, CancellationToken.None).AsTask());
        }
        Assert.ContainsSingle(document.Layers);
    }

    [TestMethod]
    public void OlderLayerJson_DefaultsToNoColorLabel()
    {
        AnnotationLayer? layer = JsonSerializer.Deserialize<AnnotationLayer>(
            "{\"name\":\"Existing layer\",\"isVisible\":true}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(layer);
        Assert.AreEqual(LayerColorLabel.None, layer.ColorLabel);
        Assert.AreEqual("Existing layer", layer.Name);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LayerEdits_SurviveSaveAndReopen(bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        var document = new CaptureDocument { Layers = [new AnnotationLayer(), new AnnotationLayer()] };
        AnnotationLayer target = document.Layers[0];
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new RenameAnnotationLayerCommand(target.Id, "Notes"), CancellationToken.None);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(target.Id, LayerColorLabel.Purple), CancellationToken.None);
        await stack.ExecuteAsync(document, new MoveAnnotationLayerCommand(target.Id, 1), CancellationToken.None);
        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(target.Id, loaded.Layers[^1].Id);
        Assert.AreEqual("Notes", loaded.Layers[^1].Name);
        Assert.AreEqual(LayerColorLabel.Purple, loaded.Layers[^1].ColorLabel);
    }

    [TestMethod]
    public async Task MoveLayer_ChangesRenderedOverlapAndUndoRestoresIt()
    {
        using var workspace = TemporaryWorkspace.Create();
        string source = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
        {
            bitmap.Save(source, ImageFormat.Png);
        }
        var bottom = new AnnotationLayer();
        var top = new AnnotationLayer();
        var document = new CaptureDocument
        {
            Layers = [bottom, top],
            SourceImage = new ImageAsset(source, 8, 8, ImagePixelFormat.Bgra32),
            Annotations = [Rectangle(bottom.Id, new ColorRgba(255, 0, 0, 255)), Rectangle(top.Id, new ColorRgba(0, 0, 255, 255))]
        };
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        var stack = new EditCommandStack();
        await CheckPixel(Color.Blue);
        await stack.ExecuteAsync(document, new SetLayerColorCommand(top.Id, LayerColorLabel.Red), CancellationToken.None);
        await CheckPixel(Color.Blue);
        await stack.ExecuteAsync(document, new MoveAnnotationLayerCommand(bottom.Id, 1), CancellationToken.None);
        await CheckPixel(Color.Red);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        await CheckPixel(Color.Blue);

        async Task CheckPixel(Color expected)
        {
            await repository.SaveAsync(document, CancellationToken.None);
            RenderResult result = await renderer.RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            using var stream = new MemoryStream(result.Image!.Pixels);
            using var image = new Bitmap(stream);
            Assert.AreEqual(expected.ToArgb(), image.GetPixel(4, 4).ToArgb());
        }
    }

    private static AnnotationObject Rectangle(Guid layerId, ColorRgba fill) => new()
    {
        LayerId = layerId,
        Kind = AnnotationKind.Rectangle,
        Bounds = new RectD(0, 0, 8, 8),
        Style = new AnnotationStyle(ColorRgba.Transparent, fill, ColorRgba.Black, 1, 1)
    };
}
