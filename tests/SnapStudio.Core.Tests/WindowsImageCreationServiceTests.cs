using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Platform.Windows;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsImageCreationServiceTests
{
    [TestMethod]
    public async Task CreateBlankAsync_CreatesFullyTransparentPngAndUniqueSourceFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var service = new WindowsImageCreationService(workspace.Path);

        CaptureOutcome first = await service.CreateBlankAsync(new NewImageRequest(32, 24), CancellationToken.None);
        CaptureOutcome second = await service.CreateBlankAsync(new NewImageRequest(32, 24), CancellationToken.None);

        Assert.IsNotNull(first.Capture);
        Assert.IsNotNull(second.Capture);
        Assert.AreNotEqual(first.Capture.SourceImage.Path, second.Capture.SourceImage.Path);
        Assert.AreEqual("new", first.Capture.Metadata["source"]);
        using var bitmap = new Bitmap(first.Capture.SourceImage.Path);
        Assert.AreEqual(32, bitmap.Width);
        Assert.AreEqual(24, bitmap.Height);
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Assert.AreEqual(0, bitmap.GetPixel(x, y).A);
            }
        }
    }

    [TestMethod]
    [DataRow(0, 720)]
    [DataRow(-1, 720)]
    [DataRow(1280, 20001)]
    [DataRow(20000, 20000)]
    public async Task CreateBlankAsync_InvalidDimensionsLeaveNoFiles(int width, int height)
    {
        using var workspace = TemporaryWorkspace.Create();
        var service = new WindowsImageCreationService(workspace.Path);

        CaptureOutcome outcome = await service.CreateBlankAsync(new NewImageRequest(width, height), CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        Assert.IsNotNull(outcome.Failure);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path));
    }

    [TestMethod]
    public async Task PasteAsync_PreservesTransparentAndTranslucentPixelsInIndependentPng()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var source = new Bitmap(12, 10, PixelFormat.Format32bppArgb);
        source.SetPixel(3, 4, Color.FromArgb(96, 20, 90, 180));
        source.SetPixel(5, 6, Color.Red);
        using var encoded = new MemoryStream();
        source.Save(encoded, ImageFormat.Png);
        byte[] bytes = encoded.ToArray();
        var clipboardStream = new MemoryStream(bytes);
        var service = new WindowsImageCreationService(workspace.Path, _ => Task.FromResult<Stream?>(clipboardStream));

        CaptureOutcome outcome = await service.PasteAsync(CancellationToken.None);

        Assert.IsNotNull(outcome.Capture);
        Assert.IsFalse(clipboardStream.CanRead);
        Assert.AreEqual("clipboard", outcome.Capture.Metadata["source"]);
        using var pasted = new Bitmap(outcome.Capture.SourceImage.Path);
        Assert.AreEqual(12, pasted.Width);
        Assert.AreEqual(10, pasted.Height);
        Assert.AreEqual(0, pasted.GetPixel(0, 0).A);
        Assert.AreEqual(96, pasted.GetPixel(3, 4).A);
        Assert.AreEqual(Color.Red.ToArgb(), pasted.GetPixel(5, 6).ToArgb());
    }

    [TestMethod]
    public async Task PasteAsync_EmptyClipboardReturnsHelpfulFailureWithoutFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var service = new WindowsImageCreationService(workspace.Path, _ => Task.FromResult<Stream?>(null));

        CaptureOutcome outcome = await service.PasteAsync(CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Failure!.Message, "does not contain an image");
        Assert.IsEmpty(Directory.GetFiles(workspace.Path));
    }

    [TestMethod]
    public async Task PasteAsync_CorruptImageReturnsFailureWithoutFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var service = new WindowsImageCreationService(workspace.Path,
            _ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3, 4])));

        CaptureOutcome outcome = await service.PasteAsync(CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        Assert.IsNotNull(outcome.Failure!.Exception);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path));
    }

    [TestMethod]
    public async Task ReadClipboardImageAsync_PreservesAlphaWithoutCreatingSourceFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var source = new Bitmap(12, 10, PixelFormat.Format32bppArgb);
        source.SetPixel(3, 4, Color.FromArgb(96, 20, 90, 180));
        source.SetPixel(5, 6, Color.Red);
        using var encoded = new MemoryStream();
        source.Save(encoded, ImageFormat.Png);
        var clipboardStream = new MemoryStream(encoded.ToArray());
        var service = new WindowsImageCreationService(workspace.Path, _ => Task.FromResult<Stream?>(clipboardStream));

        ClipboardImageResult result = await service.ReadClipboardImageAsync(CancellationToken.None);

        Assert.IsNotNull(result.Image, result.ErrorMessage);
        Assert.IsFalse(clipboardStream.CanRead);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path));
        using var stream = new MemoryStream(result.Image.PngBytes);
        using var image = new Bitmap(stream);
        Assert.AreEqual(12, result.Image.Width);
        Assert.AreEqual(10, result.Image.Height);
        Assert.AreEqual(0, image.GetPixel(0, 0).A);
        Assert.AreEqual(96, image.GetPixel(3, 4).A);
        Assert.AreEqual(Color.Red.ToArgb(), image.GetPixel(5, 6).ToArgb());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadClipboardImageAsync_EmptyOrCorruptClipboardReturnsFailureWithoutFiles(bool corrupt)
    {
        using var workspace = TemporaryWorkspace.Create();
        var service = new WindowsImageCreationService(workspace.Path,
            _ => Task.FromResult<Stream?>(corrupt ? new MemoryStream([1, 2, 3]) : null));

        ClipboardImageResult result = await service.ReadClipboardImageAsync(CancellationToken.None);

        Assert.IsNull(result.Image);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.IsEmpty(Directory.GetFiles(workspace.Path));
    }

    [TestMethod]
    public async Task CancelledOperationsDoNotReadClipboardOrCreateFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        bool clipboardRead = false;
        var service = new WindowsImageCreationService(workspace.Path, _ =>
        {
            clipboardRead = true;
            return Task.FromResult<Stream?>(null);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.CreateBlankAsync(new NewImageRequest(32, 24), cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.PasteAsync(cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ReadClipboardImageAsync(cancellation.Token));

        Assert.IsFalse(clipboardRead);
        Assert.IsEmpty(Directory.GetFiles(workspace.Path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NewDocument_RetainsTransparencyThroughStorageAnnotationResizeAndPngExport(bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var service = new WindowsImageCreationService(Path.Combine(workspace.Path, "SourceImages"));
        CaptureOutcome created = await service.CreateBlankAsync(new NewImageRequest(32, 24), CancellationToken.None);
        Assert.IsNotNull(created.Capture);
        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        CaptureDocument document = await repository.CreateFromCaptureAsync(created.Capture, CancellationToken.None);
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            LayerId = document.Layers[0].Id,
            Bounds = new RectD(8, 6, 12, 10),
            Style = new AnnotationStyle(ColorRgba.Transparent, new ColorRgba(255, 0, 0, 255), ColorRgba.Black, 1, 1)
        });
        await repository.SaveAsync(document, CancellationToken.None);

        var renderer = new SystemDrawingDocumentRenderer(repository);
        var exporter = new RenderedDocumentExportProvider(ExportFormat.Png, renderer);
        string outputPath = Path.Combine(workspace.Path, "export.png");
        ExportResult exported = await exporter.ExportAsync(
            new ExportRequest(document.Id, ExportFormat.Png, outputPath, new Dictionary<string, string>()),
            CancellationToken.None);
        Assert.IsTrue(exported.Succeeded, exported.ErrorMessage);
        using (var exportedBitmap = new Bitmap(outputPath))
        {
            Assert.AreEqual(0, exportedBitmap.GetPixel(0, 0).A);
            Assert.AreEqual(255, exportedBitmap.GetPixel(12, 10).A);
            Assert.AreEqual(Color.Red.ToArgb(), exportedBitmap.GetPixel(12, 10).ToArgb());
        }

        var rasterEditor = new SystemDrawingDocumentRasterEditor();
        var resized = await rasterEditor.ResizeAsync(
            new Core.Rendering.RasterResizeRequest(document.SourceImage, 64, 48, workspace.Path),
            CancellationToken.None);
        Assert.IsTrue(resized.Succeeded, resized.ErrorMessage);
        using var resizedBitmap = new Bitmap(resized.Image!.Path);
        Assert.AreEqual(0, resizedBitmap.GetPixel(32, 24).A);
    }

    [TestMethod]
    [DataRow(double.NaN, 720d)]
    [DataRow(double.PositiveInfinity, 720d)]
    [DataRow(10.5d, 720d)]
    [DataRow(720d, 0d)]
    public void NewImageRequest_RejectsEmptyFractionalAndNonFiniteDimensions(double width, double height)
    {
        Assert.IsNotNull(NewImageRequest.GetValidationError(width, height));
    }

    [TestMethod]
    public void NewImageRequest_AcceptsSmallImagesAndExactPixelLimit()
    {
        Assert.IsNull(NewImageRequest.GetValidationError(1, 1));
        Assert.IsNull(NewImageRequest.GetValidationError(20000, 2000));
        Assert.IsNotNull(NewImageRequest.GetValidationError(20000, 2001));
    }
}
