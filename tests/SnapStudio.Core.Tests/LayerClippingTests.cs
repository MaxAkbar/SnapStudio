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
public sealed class LayerClippingTests
{
    [TestMethod]
    [DataRow(PixelSelectionTool.Rectangle)]
    [DataRow(PixelSelectionTool.Ellipse)]
    [DataRow(PixelSelectionTool.Lasso)]
    public async Task MaskPreservesSelectionHolesOtherLayersAndSource(PixelSelectionTool tool)
    {
        using var workspace = TemporaryWorkspace.Create();
        var document = CreateDocument(workspace.Path);
        byte[] original = await File.ReadAllBytesAsync(document.SourceImage.Path);
        PixelSelection selection = PixelSelection.Create(16, 12, tool, new(2, 2, 12, 8), [new(2, 2), new(14, 2), new(8, 10)])
            .Combine(PixelSelection.Create(16, 12, PixelSelectionTool.Rectangle, new(6, 4, 3, 2)), PixelSelectionMode.Subtract);
        document.Layers[1].Mask = new(selection.GetRectangles());
        using Bitmap bitmap = await Render(document);
        Assert.AreEqual(16, bitmap.Width); Assert.AreEqual(12, bitmap.Height);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++)
            Assert.AreEqual((selection.Contains(x, y) ? Color.Lime : Color.Red).ToArgb(), bitmap.GetPixel(x, y).ToArgb(), $"{tool} pixel {x},{y}");
        Assert.AreEqual(new RectD(0, 0, 16, 12), document.Annotations[1].Bounds);
        Assert.AreSequenceEqual(original, await File.ReadAllBytesAsync(document.SourceImage.Path));
    }

    [TestMethod]
    public async Task LayerCropIntersectsMaskWithoutResizingDocumentOrOtherLayer()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        document.Layers[1].Mask = new([new(2, 2, 10, 8)]); document.Layers[1].CropBounds = new(6, 0, 10, 6);
        using Bitmap bitmap = await Render(document);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++)
            Assert.AreEqual((x >= 6 && x < 12 && y >= 2 && y < 6 ? Color.Lime : Color.Red).ToArgb(), bitmap.GetPixel(x, y).ToArgb());
        Assert.AreEqual(16, bitmap.Width); Assert.AreEqual(12, bitmap.Height);
        Assert.AreEqual(new RectD(0, 0, 16, 12), document.Annotations[0].Bounds);
    }

    [TestMethod]
    public async Task MaskChangesUndoRedoRemoveAndDisableWithoutLosingArtwork()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        var stack = new EditCommandStack(); var layer = document.Layers[1];
        RectD[] regions = [new(2, 2, 4, 4)];
        await stack.ExecuteAsync(document, new SetLayerClippingCommand(layer.Id, new(regions), null, "Add Layer Mask"), default);
        regions[0] = new(0, 0, 1, 1);
        Assert.AreEqual(new RectD(2, 2, 4, 4), layer.Mask!.Rectangles[0]);
        Assert.IsTrue(await stack.UndoAsync(document, default)); Assert.IsNull(layer.Mask); Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(await stack.RedoAsync(document, default)); Assert.IsNotNull(layer.Mask);
        await stack.ExecuteAsync(document, new SetLayerClippingCommand(layer.Id, layer.Mask with { IsEnabled = false }, new(1, 1, 12, 8), "Disable Mask"), default);
        using (Bitmap disabled = await Render(document)) { Assert.AreEqual(Color.Lime.ToArgb(), disabled.GetPixel(10, 5).ToArgb()); Assert.AreEqual(Color.Red.ToArgb(), disabled.GetPixel(0, 0).ToArgb()); }
        await stack.ExecuteAsync(document, new SetLayerClippingCommand(layer.Id, null, null, "Reset"), default);
        using (Bitmap reset = await Render(document)) Assert.AreEqual(Color.Lime.ToArgb(), reset.GetPixel(0, 0).ToArgb());
        await stack.UndoAsync(document, default); Assert.IsNotNull(layer.Mask); Assert.IsFalse(layer.Mask.IsEnabled); Assert.IsNotNull(layer.CropBounds);
        Assert.HasCount(2, document.Annotations);
    }

    [TestMethod]
    public async Task SaveReopenDuplicateAndSchemaMigrationPreserveMasks()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        document.Layers[1].Mask = new([new(1, 1, 4, 3), new(8, 6, 4, 3)], false); document.Layers[1].CropBounds = new(0, 0, 12, 10);
        var repository = new FileSystemDocumentRepository(Path.Combine(workspace.Path, "documents"));
        await repository.SaveAsync(document, default); var reopened = await repository.GetAsync(document.Id, default);
        Assert.IsNotNull(reopened); Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, reopened.SchemaVersion);
        Assert.AreSequenceEqual(document.Layers[1].Mask!.Rectangles, reopened.Layers[1].Mask!.Rectangles);
        Assert.IsFalse(reopened.Layers[1].Mask!.IsEnabled); Assert.AreEqual(document.Layers[1].CropBounds, reopened.Layers[1].CropBounds);
        var duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(reopened, "Copy");
        Assert.AreSequenceEqual(reopened.Layers[1].Mask!.Rectangles, duplicate.Layers[1].Mask!.Rectangles);
        Assert.AreNotSame(reopened.Layers[1].Mask!.Rectangles, duplicate.Layers[1].Mask!.Rectangles);
        var legacy = new CaptureDocument { SchemaVersion = 8 }; DocumentLayers.EnsureInitialized(legacy);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, legacy.SchemaVersion); Assert.IsNull(legacy.Layers[0].Mask);
    }

    [TestMethod]
    public async Task RasterResizeAndCropTransformMasksAndUndoRestoresThem()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        document.Layers[1].Mask = new([new(4, 2, 8, 8)]); document.Layers[1].CropBounds = new(2, 2, 12, 10);
        var stack = new EditCommandStack(); var original = document.SourceImage;
        var after = LayerClipping.Transform(document.Layers, 0, 0, .5, .5, 8, 6);
        await stack.ExecuteAsync(document, new UpdateDocumentRasterCommand(original, original with { Width = 8, Height = 6 }, [], [], [], [], "Resize", document.Layers, after), default);
        DocumentLayers.ValidateForSave(document); Assert.AreEqual(new RectD(2, 1, 4, 4), document.Layers[1].Mask!.Rectangles[0]);
        await stack.UndoAsync(document, default); Assert.AreEqual(new RectD(4, 2, 8, 8), document.Layers[1].Mask!.Rectangles[0]);
        after = LayerClipping.Transform(document.Layers, -6, -4, 1, 1, 6, 6);
        await stack.ExecuteAsync(document, new UpdateDocumentRasterCommand(original, original with { Width = 6, Height = 6 }, [], [], [], [], "Crop", document.Layers, after), default);
        DocumentLayers.ValidateForSave(document); Assert.AreEqual(new RectD(0, 0, 6, 6), document.Layers[1].Mask!.Rectangles[0]);
        await stack.UndoAsync(document, default); Assert.AreEqual(original, document.SourceImage); Assert.AreEqual(new RectD(2, 2, 12, 10), document.Layers[1].CropBounds);
    }

    [TestMethod]
    public async Task EmptyMaskHidesOnlyItsLayerAndMasksTrackViewportScale()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        document.Layers[1].Mask = new([]);
        using (Bitmap hidden = await Render(document)) Assert.AreEqual(Color.Red.ToArgb(), hidden.GetPixel(5, 5).ToArgb());
        document.Layers[1].Mask = new([new(6, 4, 4, 4)]);
        using Bitmap scaled = await Render(document, new(document.Id, 2, new(4, 2, 8, 8)));
        Assert.AreEqual(Color.Red.ToArgb(), scaled.GetPixel(1, 1).ToArgb()); Assert.AreEqual(Color.Lime.ToArgb(), scaled.GetPixel(6, 6).ToArgb());
    }

    [TestMethod]
    public async Task BlurRespectsMaskInsteadOfChangingUnselectedPixels()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        document.Annotations.Clear();
        using (var image = new Bitmap(16, 12)) { for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++) image.SetPixel(x, y, x % 2 == 0 ? Color.Black : Color.White); image.Save(document.SourceImage.Path, ImageFormat.Png); }
        using Bitmap original = await Render(document);
        document.Layers[1].Mask = new([new(0, 0, 8, 12)]);
        document.Annotations.Add(new() { Kind = AnnotationKind.Blur, LayerId = document.Layers[1].Id, Bounds = new(0, 0, 16, 12) });
        using Bitmap blurred = await Render(document);
        for (int y = 0; y < 12; y++) for (int x = 8; x < 16; x++) Assert.AreEqual(original.GetPixel(x, y).ToArgb(), blurred.GetPixel(x, y).ToArgb());
        Assert.AreNotEqual(original.GetPixel(4, 4).ToArgb(), blurred.GetPixel(4, 4).ToArgb());
    }

    [TestMethod]
    public void InvalidRegionsAreRejectedAndOutlineMovementLeavesArtworkAlone()
    {
        var document = new CaptureDocument { SourceImage = new("unused", 16, 12, ImagePixelFormat.Bgra32) };
        document.Layers[0].Mask = new([new(double.NaN, 0, 2, 2)]); Assert.Throws<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        document.Layers[0].Mask = new([new(15, 0, 2, 2)]); Assert.Throws<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        var selection = PixelSelection.Create(16, 12, PixelSelectionTool.Ellipse, new(2, 2, 8, 6));
        var moved = selection.Move(3, 2); Assert.AreEqual(selection.PixelCount, moved.PixelCount);
        foreach (var span in selection.Spans) for (int x = span.Left; x < span.Right; x++) Assert.IsTrue(moved.Contains(x + 3, span.Y + 2));
        Assert.IsEmpty(document.Annotations);
    }

    private static CaptureDocument CreateDocument(string directory)
    {
        string path = Path.Combine(directory, "source.png");
        using (var bitmap = new Bitmap(16, 12)) { using Graphics graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Blue); bitmap.Save(path, ImageFormat.Png); }
        var document = new CaptureDocument { SourceImage = new(path, 16, 12, ImagePixelFormat.Bgra32), Layers = [new(), new()] };
        foreach (var (layer, color) in document.Layers.Zip(new[] { new ColorRgba(255, 0, 0, 255), new ColorRgba(0, 255, 0, 255) }))
        { var fill = PixelSelection.Create(16, 12, PixelSelectionTool.Rectangle, new(0, 0, 16, 12)).CreateFill(color); fill.LayerId = layer.Id; document.Annotations.Add(fill); }
        return document;
    }

    private static async Task<Bitmap> Render(CaptureDocument document, RenderRequest? request = null)
    {
        var result = await new SystemDrawingDocumentRenderer(new Repository(document)).RenderAsync(request ?? new(document.Id, 1, null), default);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage); using var stream = new MemoryStream(result.Image!.Pixels); using var image = new Bitmap(stream); return new Bitmap(image);
    }

    private sealed class Repository(CaptureDocument document) : IDocumentRepository
    {
        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult<CaptureDocument?>(document);
        public Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<CaptureDocument> CreateFromCaptureAsync(CaptureResult capture, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
