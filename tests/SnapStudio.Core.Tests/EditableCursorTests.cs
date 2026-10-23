using System.Drawing;
using System.Drawing.Imaging;
using CSharpDB.Data;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Platform.Windows;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class EditableCursorTests
{
    [TestMethod]
    [DataRow(-3840, 0, -3000, 200, 840, 200)]
    [DataRow(-3840, -2160, 200, 100, 4040, 2260)]
    [DataRow(100, 200, 98, 198, -2, -2)]
    public void CursorMapsPhysicalDesktopCoordinatesAndRetainsUnclippedImage(int x, int y, int cursorX, int cursorY, int expectedX, int expectedY)
    {
        var snapshot = new WindowsCursorSnapshot(CursorImage(), new RectD(cursorX, cursorY, 8, 8), 42);
        CapturedCursor? cursor = snapshot.ForCapture(new RectD(x, y, 7680, 4320));
        Assert.IsNotNull(cursor);
        Assert.AreEqual(new RectD(expectedX, expectedY, 8, 8), cursor.Bounds);
    }

    [TestMethod]
    public void CursorOutsideCaptureOrOnAnOccludingWindowIsExcluded()
    {
        var snapshot = new WindowsCursorSnapshot(CursorImage(), new RectD(100, 100, 8, 8), 42);
        Assert.IsNull(snapshot.ForCapture(new RectD(108, 100, 100, 100)));
        Assert.IsNull(snapshot.ForCapture(new RectD(0, 0, 100, 100)));
        Assert.IsNull(snapshot.ForCapture(new RectD(0, 0, 200, 200), 43));
        Assert.IsNotNull(snapshot.ForCapture(new RectD(0, 0, 200, 200), 42));
    }

    [TestMethod]
    public void TransparencyRecoveryPreservesOpaqueAntialiasedAndTransparentPixels()
    {
        using var black = new Bitmap(3, 1);
        using var white = new Bitmap(3, 1);
        black.SetPixel(0, 0, Color.Black); white.SetPixel(0, 0, Color.White);
        black.SetPixel(1, 0, Color.FromArgb(255, 80, 20, 0)); white.SetPixel(1, 0, Color.FromArgb(255, 207, 147, 127));
        black.SetPixel(2, 0, Color.White); white.SetPixel(2, 0, Color.White);
        using Bitmap result = WindowsCursorCapture.ExtractTransparency(black, white, () => throw new AssertFailedException("No XOR pixels expected."));
        Assert.AreEqual(0, result.GetPixel(0, 0).A);
        Color edge = result.GetPixel(1, 0);
        Assert.AreEqual(128, edge.A);
        Assert.IsInRange(158, 160, (int)edge.R);
        Assert.AreEqual(255, result.GetPixel(2, 0).A);
        Assert.AreEqual(255, result.GetPixel(2, 0).R);
    }

    [TestMethod]
    public void LegacyInvertedCursorUsesCapturedBackdropOnlyForXorPixels()
    {
        using var black = new Bitmap(2, 1);
        using var white = new Bitmap(2, 1);
        black.SetPixel(0, 0, Color.White); white.SetPixel(0, 0, Color.Black);
        black.SetPixel(1, 0, Color.Black); white.SetPixel(1, 0, Color.White);
        int calls = 0;
        using Bitmap result = WindowsCursorCapture.ExtractTransparency(black, white, () =>
        {
            calls++;
            var backdrop = new Bitmap(2, 1);
            backdrop.SetPixel(0, 0, Color.Cyan);
            return backdrop;
        });
        Assert.AreEqual(1, calls);
        Assert.AreEqual(Color.Cyan.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.AreEqual(0, result.GetPixel(1, 0).A);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CaptureCreatesEditablePointerLayerAndReopensItsEmbeddedImage(bool database)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        IDocumentRepository repository = Repository(workspace.Path, database);
        var cursor = new CapturedCursor(CursorImage(), new RectD(10, 20, 8, 8));
        var capture = new CaptureResult(CaptureId.New(), DateTimeOffset.UtcNow,
            new ImageAsset("source.png", 100, 100, ImagePixelFormat.Bgra32), new Dictionary<string, string>(), cursor);
        CaptureDocument document = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
        Assert.AreEqual(2, document.Layers.Count);
        Assert.AreEqual("Mouse pointer", document.Layers[1].Name);
        Assert.AreEqual(document.Layers[1].Id, document.Annotations.Single().LayerId);
        Assert.AreEqual(AnnotationKind.Image, document.Annotations[0].Kind);
        Assert.AreNotSame(cursor.Image.PngBytes, document.Annotations[0].Image!.PngBytes);
        if (database) await CSharpDbConnection.ClearPoolAsync(new CSharpDbConnectionStringBuilder
        {
            DataSource = Path.Combine(workspace.Path, "snapstudio-documents.db"),
            Pooling = true
        }.ConnectionString);
        CaptureDocument? reopened = await Repository(workspace.Path, database).GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(reopened);
        Assert.AreEqual(cursor.Bounds, reopened.Annotations[0].Bounds);
        Assert.AreSequenceEqual(cursor.Image.PngBytes, reopened.Annotations[0].Image!.PngBytes);
        Assert.AreEqual("Mouse pointer", reopened.Layers[1].Name);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CursorOffDoesNotCreateAnEmptyPointerLayer(bool database)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var capture = new CaptureResult(CaptureId.New(), DateTimeOffset.UtcNow,
            new ImageAsset("source.png", 100, 100, ImagePixelFormat.Bgra32), new Dictionary<string, string>());
        CaptureDocument document = await Repository(workspace.Path, database).CreateFromCaptureAsync(capture, CancellationToken.None);
        Assert.HasCount(1, document.Layers);
        Assert.IsEmpty(document.Annotations);
    }

    [TestMethod]
    public async Task PointerMoveResizeOpacityHideDeleteAndUndoRenderAgainstACleanSource()
    {
        using var workspace = TemporaryWorkspace.Create();
        CaptureDocument document = Document(workspace.Path);
        AnnotationObject pointer = document.Annotations[0];
        var repository = new FileSystemDocumentRepository(workspace.Path);
        var stack = new EditCommandStack();
        using (Bitmap original = await Render(repository, document))
        {
            Assert.IsTrue(original.GetPixel(11, 11).A > 240);
            Assert.AreEqual(0, original.GetPixel(16, 16).A);
        }
        await stack.ExecuteAsync(document, new UpdateAnnotationBoundsCommand(pointer.Id, pointer.Bounds, new RectD(24, 20, 16, 16)), CancellationToken.None);
        pointer.Style = pointer.Style with { Opacity = 0.5 };
        using (Bitmap moved = await Render(repository, document))
        {
            Assert.AreEqual(0, moved.GetPixel(11, 11).A);
            Assert.IsInRange(120, 135, (int)moved.GetPixel(27, 23).A);
            Assert.AreEqual(0, moved.GetPixel(36, 32).A);
        }
        document.Layers[1].IsVisible = false;
        using (Bitmap hidden = await Render(repository, document)) Assert.AreEqual(0, hidden.GetPixel(27, 23).A);
        document.Layers[1].IsVisible = true;
        pointer.IsVisible = false;
        using (Bitmap hidden = await Render(repository, document)) Assert.AreEqual(0, hidden.GetPixel(27, 23).A);
        pointer.IsVisible = true;
        await stack.ExecuteAsync(document, new RemoveAnnotationCommand(pointer), CancellationToken.None);
        using (Bitmap deleted = await Render(repository, document)) Assert.AreEqual(0, deleted.GetPixel(27, 23).A);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        using (Bitmap restored = await Render(repository, document)) Assert.IsTrue(restored.GetPixel(27, 23).A > 120);
        using var source = new Bitmap(document.SourceImage.Path);
        Assert.AreEqual(0, source.GetPixel(11, 11).A);
    }

    [TestMethod]
    public async Task PartiallyClippedPointerAndScaledViewportKeepImageShape()
    {
        using var workspace = TemporaryWorkspace.Create();
        CaptureDocument document = Document(workspace.Path);
        document.Annotations[0].Bounds = new RectD(-2, -2, 8, 8);
        var repository = new FileSystemDocumentRepository(workspace.Path);
        using (Bitmap clipped = await Render(repository, document))
        {
            Assert.IsTrue(clipped.GetPixel(0, 0).A > 240);
            Assert.AreEqual(0, clipped.GetPixel(4, 4).A);
        }
        await repository.SaveAsync(document, CancellationToken.None);
        RenderResult result = await new SystemDrawingDocumentRenderer(repository).RenderAsync(new RenderRequest(document.Id, 2, new RectD(0, 0, 10, 10)), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var stream = new MemoryStream(result.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.AreEqual(20, image.Width);
        Assert.IsTrue(image.GetPixel(0, 0).A > 200);
        Assert.AreEqual(0, image.GetPixel(10, 10).A);
    }

    [TestMethod]
    public async Task DuplicateAndRasterUndoHaveIndependentEmbeddedImageBytes()
    {
        using var workspace = TemporaryWorkspace.Create();
        CaptureDocument document = Document(workspace.Path);
        byte original = document.Annotations[0].Image!.PngBytes[0];
        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(document, "Copy");
        duplicate.Annotations[0].Image!.PngBytes[0] = 0;
        Assert.AreEqual(original, document.Annotations[0].Image!.PngBytes[0]);
        var command = new UpdateDocumentRasterCommand(document.SourceImage, document.SourceImage, document.Annotations, document.Annotations, [], [], "Resize");
        await command.ApplyAsync(document, CancellationToken.None);
        document.Annotations[0].Image!.PngBytes[0] = 0;
        await command.RevertAsync(document, CancellationToken.None);
        Assert.AreEqual(original, document.Annotations[0].Image!.PngBytes[0]);
    }

    [TestMethod]
    public void OlderDocumentsMigrateAndImageObjectsRejectMissingDataOrInvalidBounds()
    {
        var document = new CaptureDocument { SchemaVersion = 4 };
        Guid layerId = document.Layers[0].Id;
        DocumentLayers.EnsureInitialized(document);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(layerId, document.Layers[0].Id);
        CapturedCursorLayer.AddTo(document, new CapturedCursor(CursorImage(), new RectD(0, 0, 8, 8)));
        document.Annotations[0].Bounds = new RectD(double.NaN, 0, 8, 8);
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        document.Annotations[0].Bounds = new RectD(0, 0, 8, 8);
        document.Annotations[0].Image = null;
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
    }

    private static IDocumentRepository Repository(string path, bool database) => database
        ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(path)) : new FileSystemDocumentRepository(path);

    [TestMethod]
    [DataRow(0)]
    [DataRow(500)]
    [DataRow(1000)]
    [DataRow(1500)]
    [DataRow(2000)]
    [DataRow(3000)]
    [DataRow(6000)]
    public async Task LargePointerRepeatedEditsDeleteAndNewCaptureRemainReadable(int padding)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var repository = Repository(workspace.Path, true);
        await repository.SaveAsync(new CaptureDocument { SourceImage = new ImageAsset("seed://welcome", 1280, 720, ImagePixelFormat.Unknown) }, CancellationToken.None);
        using var bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, x < 4 && y < 4 ? Color.Red : Color.Transparent);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var capture = new CaptureResult(CaptureId.New(), DateTimeOffset.UtcNow,
            new ImageAsset(Path.Combine(workspace.Path, "SourceImages", "7993bb6f-6bf9-4312-a4c8-b68fd2b8e141.png"), 7680, 2160, ImagePixelFormat.Bgra32), new Dictionary<string, string> { ["metadataPadding"] = new string('x', padding) },
            new CapturedCursor(new AnnotationImage(48, 48, stream.ToArray()), new RectD(0, 0, 48, 48)));
        for (int repeat = 0; repeat < 3; repeat++)
        {
            CaptureDocument document = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
            for (int edit = 0; edit < 24; edit++)
            {
                document.Annotations[0].IsVisible = edit % 2 == 0;
                document.Annotations[0].Bounds = new RectD(edit, edit, 48, 48);
                await repository.SaveAsync(document, CancellationToken.None);
                Assert.IsNotNull(await repository.GetAsync(document.Id, CancellationToken.None));
            }
            Assert.IsTrue(await repository.DeleteAsync(document.Id, CancellationToken.None));
            Assert.IsNull(await repository.GetAsync(document.Id, CancellationToken.None));
            Assert.HasCount(1, await new CSharpDbDocumentCatalog(new CSharpDbDocumentStore(workspace.Path)).GetRecentAsync(25, CancellationToken.None));
        }
        CaptureDocument final = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
        Assert.AreSequenceEqual(capture.Cursor!.Image.PngBytes,
            (await repository.GetAsync(final.Id, CancellationToken.None))!.Annotations[0].Image!.PngBytes);
    }

    private static CaptureDocument Document(string directory)
    {
        string path = Path.Combine(directory, "source.png");
        using (var source = new Bitmap(64, 64, PixelFormat.Format32bppArgb)) source.Save(path, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(path, 64, 64, ImagePixelFormat.Bgra32) };
        CapturedCursorLayer.AddTo(document, new CapturedCursor(CursorImage(), new RectD(10, 10, 8, 8)));
        return document;
    }

    private static AnnotationImage CursorImage()
    {
        using var bitmap = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) bitmap.SetPixel(x, y, Color.Red);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new AnnotationImage(8, 8, stream.ToArray());
    }

    private static async Task<Bitmap> Render(IDocumentRepository repository, CaptureDocument document)
    {
        await repository.SaveAsync(document, CancellationToken.None);
        RenderResult result = await new SystemDrawingDocumentRenderer(repository).RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var stream = new MemoryStream(result.Image!.Pixels);
        using var bitmap = new Bitmap(stream);
        return new Bitmap(bitmap);
    }
}
