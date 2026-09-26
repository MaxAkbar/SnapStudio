using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class SystemDrawingDocumentRendererTests
{
    [TestMethod]
    public async Task RenderAsync_CompositesRectangleAnnotation()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 32, 32, Color.White);
        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 32, 32, ImagePixelFormat.Bgra32),
            Annotations =
            [
                new AnnotationObject
                {
                    Kind = AnnotationKind.Rectangle,
                    Bounds = new RectD(4, 4, 12, 12),
                    Style = new AnnotationStyle(
                        new ColorRgba(196, 43, 28, 255),
                        ColorRgba.Transparent,
                        ColorRgba.Black,
                        4,
                        1)
                }
            ]
        };
        var renderer = new SystemDrawingDocumentRenderer(new FakeDocumentRepository(document));

        RenderResult result = await renderer.RenderAsync(
            new RenderRequest(document.Id, 1, null),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Image);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Color borderPixel = rendered.GetPixel(4, 4);
        Assert.IsTrue(borderPixel.R > 120);
        Assert.IsTrue(borderPixel.G < 90);
        Assert.IsTrue(borderPixel.B < 90);
    }

    [TestMethod]
    public async Task RenderAsync_SkipsHiddenAnnotation()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 32, 32, Color.White);
        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 32, 32, ImagePixelFormat.Bgra32),
            Annotations =
            [
                new AnnotationObject
                {
                    Kind = AnnotationKind.Rectangle,
                    IsVisible = false,
                    Bounds = new RectD(4, 4, 20, 20),
                    Style = new AnnotationStyle(
                        ColorRgba.Transparent,
                        new ColorRgba(196, 43, 28, 255),
                        ColorRgba.Black,
                        1,
                        1)
                }
            ]
        };
        var renderer = new SystemDrawingDocumentRenderer(new FakeDocumentRepository(document));

        RenderResult result = await renderer.RenderAsync(
            new RenderRequest(document.Id, 1, null),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Assert.AreEqual(Color.White.ToArgb(), rendered.GetPixel(12, 12).ToArgb());
    }

    [TestMethod]
    public async Task RenderAsync_UsesLayerOrderAndHidesEveryObjectInHiddenLayer()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 32, 32, Color.White);
        var bottom = new AnnotationLayer { Name = "Bottom" };
        var top = new AnnotationLayer { Name = "Top" };
        AnnotationObject topRed = CreateFilledRectangle(
            new RectD(4, 4, 20, 20), new ColorRgba(220, 0, 0, 255));
        AnnotationObject topGreen = CreateFilledRectangle(
            new RectD(24, 4, 6, 20), new ColorRgba(0, 220, 0, 255));
        AnnotationObject bottomBlue = CreateFilledRectangle(
            new RectD(4, 4, 20, 20), new ColorRgba(0, 0, 220, 255));
        topRed.LayerId = top.Id;
        topGreen.LayerId = top.Id;
        bottomBlue.LayerId = bottom.Id;
        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 32, 32, ImagePixelFormat.Bgra32),
            Layers = [bottom, top],
            Annotations = [topRed, topGreen, bottomBlue]
        };
        var renderer = new SystemDrawingDocumentRenderer(new FakeDocumentRepository(document));

        RenderResult shown = await renderer.RenderAsync(
            new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(shown.Succeeded);
        using (Bitmap bitmap = LoadBitmap(shown.Image!.Pixels))
        {
            Assert.IsTrue(bitmap.GetPixel(12, 12).R > 180);
            Assert.IsTrue(bitmap.GetPixel(26, 12).G > 180);
        }

        top.IsVisible = false;
        RenderResult hidden = await renderer.RenderAsync(
            new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(hidden.Succeeded);
        using Bitmap hiddenBitmap = LoadBitmap(hidden.Image!.Pixels);
        Assert.IsTrue(hiddenBitmap.GetPixel(12, 12).B > 180);
        Assert.AreEqual(Color.White.ToArgb(), hiddenBitmap.GetPixel(26, 12).ToArgb());
    }

    [TestMethod]
    public async Task RenderAsync_BlursRegionAnnotation()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSplitImage(sourcePath, 32, 32);
        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 32, 32, ImagePixelFormat.Bgra32),
            Annotations =
            [
                new AnnotationObject
                {
                    Kind = AnnotationKind.Blur,
                    Bounds = new RectD(10, 0, 12, 32),
                    Style = new AnnotationStyle(
                        ColorRgba.Transparent,
                        ColorRgba.Transparent,
                        ColorRgba.Black,
                        1,
                        0.7)
                }
            ]
        };
        var renderer = new SystemDrawingDocumentRenderer(new FakeDocumentRepository(document));

        RenderResult result = await renderer.RenderAsync(
            new RenderRequest(document.Id, 1, null),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Color blurredCenter = rendered.GetPixel(16, 16);
        Assert.IsInRange((byte)20, (byte)235, blurredCenter.R);
        Assert.IsInRange((byte)20, (byte)235, blurredCenter.B);
    }

    [TestMethod]
    public async Task RenderAsync_CompositesRoundedRectangleAnnotation()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 32, 32, Color.White);
        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 32, 32, ImagePixelFormat.Bgra32),
            Annotations =
            [
                new AnnotationObject
                {
                    Kind = AnnotationKind.Rectangle,
                    Bounds = new RectD(4, 4, 20, 20),
                    Style = new AnnotationStyle(
                        ColorRgba.Transparent,
                        new ColorRgba(196, 43, 28, 255),
                        ColorRgba.Black,
                        1,
                        1,
                        8)
                }
            ]
        };
        var renderer = new SystemDrawingDocumentRenderer(new FakeDocumentRepository(document));

        RenderResult result = await renderer.RenderAsync(
            new RenderRequest(document.Id, 1, null),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Color roundedCornerPixel = rendered.GetPixel(4, 4);
        Color filledBodyPixel = rendered.GetPixel(12, 12);
        Assert.IsTrue(roundedCornerPixel.R > 240);
        Assert.IsTrue(roundedCornerPixel.G > 240);
        Assert.IsTrue(roundedCornerPixel.B > 240);
        Assert.IsTrue(filledBodyPixel.R > 120);
        Assert.IsTrue(filledBodyPixel.G < 90);
        Assert.IsTrue(filledBodyPixel.B < 90);
    }

    [TestMethod]
    public async Task RenderAsync_ScaledRenderCompositesMultipleAnnotations()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 200, 100, Color.White);
        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 200, 100, ImagePixelFormat.Bgra32),
            Annotations =
            [
                CreateFilledRectangle(
                    new RectD(20, 20, 40, 30),
                    new ColorRgba(220, 0, 0, 255)),
                CreateFilledRectangle(
                    new RectD(100, 20, 40, 30),
                    new ColorRgba(0, 0, 220, 255))
            ]
        };
        var renderer = new SystemDrawingDocumentRenderer(new FakeDocumentRepository(document));

        RenderResult result = await renderer.RenderAsync(
            new RenderRequest(document.Id, 0.5, null),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Assert.AreEqual(100, rendered.Width);
        Assert.AreEqual(50, rendered.Height);
        Color firstAnnotationPixel = rendered.GetPixel(15, 15);
        Color secondAnnotationPixel = rendered.GetPixel(55, 15);
        Assert.IsTrue(firstAnnotationPixel.R > 180);
        Assert.IsTrue(firstAnnotationPixel.B < 80);
        Assert.IsTrue(secondAnnotationPixel.B > 180);
        Assert.IsTrue(secondAnnotationPixel.R < 80);
    }

    [TestMethod]
    public async Task RasterEditor_CropsAndResizesSourceImage()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        CreateSolidImage(sourcePath, 40, 30, Color.White);
        var sourceImage = new ImageAsset(sourcePath, 40, 30, ImagePixelFormat.Bgra32);
        var editor = new SystemDrawingDocumentRasterEditor();

        RasterEditResult cropResult = await editor.CropAsync(
            new RasterCropRequest(sourceImage, new RectD(5, 6, 10, 12), workspace.Path),
            CancellationToken.None);
        RasterEditResult resizeResult = await editor.ResizeAsync(
            new RasterResizeRequest(sourceImage, 20, 15, workspace.Path),
            CancellationToken.None);

        Assert.IsTrue(cropResult.Succeeded);
        Assert.IsNotNull(cropResult.Image);
        Assert.AreEqual(10, cropResult.Image!.Width);
        Assert.AreEqual(12, cropResult.Image.Height);
        Assert.IsTrue(File.Exists(cropResult.Image.Path));

        Assert.IsTrue(resizeResult.Succeeded);
        Assert.IsNotNull(resizeResult.Image);
        Assert.AreEqual(20, resizeResult.Image!.Width);
        Assert.AreEqual(15, resizeResult.Image.Height);
        Assert.IsTrue(File.Exists(resizeResult.Image.Path));
    }

    private static void CreateSolidImage(
        string path,
        int width,
        int height,
        Color color)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static AnnotationObject CreateFilledRectangle(
        RectD bounds,
        ColorRgba fill)
    {
        return new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            Bounds = bounds,
            Style = new AnnotationStyle(
                ColorRgba.Transparent,
                fill,
                ColorRgba.Black,
                1,
                1)
        };
    }

    private static void CreateSplitImage(string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Blue);
        using var brush = new SolidBrush(Color.Red);
        graphics.FillRectangle(brush, 0, 0, width / 2, height);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static Bitmap LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    private sealed class FakeDocumentRepository(CaptureDocument document) : IDocumentRepository
    {
        public Task<CaptureDocument> CreateFromCaptureAsync(
            CaptureResult capture,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken cancellationToken)
        {
            return Task.FromResult(document.Id == id ? document : null);
        }

        public Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(DocumentId id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
