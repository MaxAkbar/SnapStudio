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
public sealed class MoveAnnotationInLayerTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public async Task Move_SwapsOnlyAdjacentSiblingsAndUndoRestoresExactGlobalOrder(int direction)
    {
        CaptureDocument document = CreateInterleavedDocument();
        AnnotationObject[] original = document.Annotations.ToArray();
        AnnotationObject target = original[2];
        RectD bounds = target.Bounds;
        AnnotationStyle style = target.Style;
        PointD[] samples = target.StrokePoints.ToArray();
        Guid[] layerOrder = document.Layers.Select(layer => layer.Id).ToArray();
        AnnotationObject[] expected = direction > 0
            ? [original[0], original[1], original[4], original[3], original[2]]
            : [original[2], original[1], original[0], original[3], original[4]];
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);

        Assert.IsTrue(await editor.MoveInLayerAsync(document, target, direction, CancellationToken.None));
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Assert.AreSequenceEqual(expected, document.Annotations);
            Assert.AreSequenceEqual(expected.Where(annotation => annotation.LayerId == target.LayerId),
                DocumentLayers.InPaintOrder(document).Where(annotation => annotation.LayerId == target.LayerId));
            Assert.AreSame(original[1], document.Annotations[1]);
            Assert.AreSame(original[3], document.Annotations[3]);
            Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
            Assert.AreSequenceEqual(original, document.Annotations);
            Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        }
        Assert.AreSequenceEqual(layerOrder, document.Layers.Select(layer => layer.Id));
        Assert.AreEqual(bounds, target.Bounds);
        Assert.AreEqual(style, target.Style);
        Assert.AreSequenceEqual(samples, target.StrokePoints);
        Assert.AreEqual(original[0].LayerId, target.LayerId);
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    [DataRow(0, -1)]
    [DataRow(4, 1)]
    public async Task Move_AtLayerBoundaryDoesNotCreateUndoOrCrossIntoAnotherLayer(int index, int direction)
    {
        CaptureDocument document = CreateInterleavedDocument();
        AnnotationObject[] original = document.Annotations.ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        Assert.IsFalse(await editor.MoveInLayerAsync(document, original[index], direction, CancellationToken.None));
        Assert.IsFalse(stack.CanUndo);
        Assert.AreSequenceEqual(original, document.Annotations);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stack.ExecuteAsync(document,
            new MoveAnnotationInLayerCommand(original[index].Id, direction), CancellationToken.None).AsTask());
        Assert.AreSequenceEqual(original, document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public async Task Move_SingleObjectCannotMoveIntoAnotherLayer(int direction)
    {
        var document = new CaptureDocument { Layers = [new AnnotationLayer(), new AnnotationLayer(), new AnnotationLayer()] };
        document.Annotations = document.Layers.Select(layer => new AnnotationObject { LayerId = layer.Id }).ToList();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        Assert.IsFalse(await editor.MoveInLayerAsync(document, document.Annotations[1], direction, CancellationToken.None));
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task Move_HiddenObjectsKeepTheirVisibilityAndParticipateInOrder()
    {
        CaptureDocument document = CreateInterleavedDocument();
        AnnotationObject target = document.Annotations[2];
        AnnotationObject neighbor = document.Annotations[4];
        target.IsVisible = false;
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new MoveAnnotationInLayerCommand(target.Id, 1), CancellationToken.None);
        Assert.AreSame(target, document.Annotations[4]);
        Assert.IsFalse(target.IsVisible);
        Assert.IsTrue(neighbor.IsVisible);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSame(target, document.Annotations[2]);
        Assert.IsFalse(target.IsVisible);
    }

    [TestMethod]
    public async Task Move_InvalidDirectionMissingObjectAndCancellationLeaveOrderAndHistoryUnchanged()
    {
        CaptureDocument document = CreateInterleavedDocument();
        AnnotationObject[] original = document.Annotations.ToArray();
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => editor.MoveInLayerAsync(document, original[2], 0, CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stack.ExecuteAsync(document,
            new MoveAnnotationInLayerCommand(Guid.NewGuid(), 1), CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => editor.MoveInLayerAsync(document,
            new AnnotationObject { Id = original[2].Id, LayerId = original[2].LayerId }, 1, CancellationToken.None).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.MoveInLayerAsync(document, original[2], 1, cancellation.Token).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new MoveAnnotationInLayerCommand(original[2].Id, 1)
            .RevertAsync(document, CancellationToken.None).AsTask());
        Assert.AreSequenceEqual(original, document.Annotations);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task Move_AndTransferBetweenLayersUndoInSequence()
    {
        CaptureDocument document = CreateInterleavedDocument();
        AnnotationObject[] original = document.Annotations.ToArray();
        AnnotationObject target = original[2];
        Guid originalLayer = target.LayerId!.Value;
        var stack = new EditCommandStack();
        await stack.ExecuteAsync(document, new MoveAnnotationInLayerCommand(target.Id, 1), CancellationToken.None);
        await stack.ExecuteAsync(document, new UpdateAnnotationLayerCommand(target.Id, originalLayer, document.Layers[1].Id), CancellationToken.None);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(original, document.Annotations);
        Assert.AreEqual(originalLayer, target.LayerId);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual(document.Layers[1].Id, target.LayerId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Move_SurvivesStorageAndChangesExportedOverlapWithUndoRedo(bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        string source = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(12, 12, PixelFormat.Format32bppArgb)) bitmap.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(source, 12, 12, ImagePixelFormat.Bgra32) };
        AnnotationObject red = Rectangle(document.Layers[0].Id, new ColorRgba(255, 0, 0, 255));
        AnnotationObject blue = Rectangle(document.Layers[0].Id, new ColorRgba(0, 0, 255, 255));
        document.Annotations = [red, blue];
        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        await Check(Color.Blue, [red.Id, blue.Id]);
        Assert.IsTrue(await editor.MoveInLayerAsync(document, red, 1, CancellationToken.None));
        await Check(Color.Red, [blue.Id, red.Id]);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        await Check(Color.Blue, [red.Id, blue.Id]);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        await Check(Color.Red, [blue.Id, red.Id]);

        async Task Check(Color expected, Guid[] expectedOrder)
        {
            await repository.SaveAsync(document, CancellationToken.None);
            CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
            Assert.IsNotNull(loaded);
            Assert.AreSequenceEqual(expectedOrder, loaded.Annotations.Select(annotation => annotation.Id));
            Assert.IsTrue(loaded.Annotations.All(annotation => annotation.LayerId == document.Layers[0].Id));
            RenderResult result = await renderer.RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            using var stream = new MemoryStream(result.Image!.Pixels);
            using var image = new Bitmap(stream);
            Assert.AreEqual(expected.ToArgb(), image.GetPixel(6, 6).ToArgb());
        }
    }

    private static CaptureDocument CreateInterleavedDocument()
    {
        var document = new CaptureDocument { Layers = [new AnnotationLayer(), new AnnotationLayer()] };
        Guid first = document.Layers[0].Id;
        Guid second = document.Layers[1].Id;
        AnnotationObject pen = FreehandStroke.Create([new(2, 3), new(8, 9), new(14, 4)], AnnotationStyle.Default);
        pen.LayerId = first;
        document.Annotations = [new() { LayerId = first }, new() { LayerId = second }, pen,
            new() { LayerId = second }, new() { LayerId = first }];
        return document;
    }

    private static AnnotationObject Rectangle(Guid layerId, ColorRgba fill) => new()
    {
        LayerId = layerId,
        Kind = AnnotationKind.Rectangle,
        Bounds = new RectD(2, 2, 8, 8),
        Style = new AnnotationStyle(ColorRgba.Transparent, fill, ColorRgba.Black, 1, 1)
    };
}
