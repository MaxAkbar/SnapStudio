using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;

namespace SnapStudio.Core.Tests;

public sealed class SystemDrawingDocumentRendererTests
{
    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Image);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Color borderPixel = rendered.GetPixel(4, 4);
        Assert.True(borderPixel.R > 120);
        Assert.True(borderPixel.G < 90);
        Assert.True(borderPixel.B < 90);
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Color blurredCenter = rendered.GetPixel(16, 16);
        Assert.InRange(blurredCenter.R, 20, 235);
        Assert.InRange(blurredCenter.B, 20, 235);
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Color roundedCornerPixel = rendered.GetPixel(4, 4);
        Color filledBodyPixel = rendered.GetPixel(12, 12);
        Assert.True(roundedCornerPixel.R > 240);
        Assert.True(roundedCornerPixel.G > 240);
        Assert.True(roundedCornerPixel.B > 240);
        Assert.True(filledBodyPixel.R > 120);
        Assert.True(filledBodyPixel.G < 90);
        Assert.True(filledBodyPixel.B < 90);
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        using Bitmap rendered = LoadBitmap(result.Image!.Pixels);
        Assert.Equal(100, rendered.Width);
        Assert.Equal(50, rendered.Height);
        Color firstAnnotationPixel = rendered.GetPixel(15, 15);
        Color secondAnnotationPixel = rendered.GetPixel(55, 15);
        Assert.True(firstAnnotationPixel.R > 180);
        Assert.True(firstAnnotationPixel.B < 80);
        Assert.True(secondAnnotationPixel.B > 180);
        Assert.True(secondAnnotationPixel.R < 80);
    }

    [Fact]
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

        Assert.True(cropResult.Succeeded);
        Assert.NotNull(cropResult.Image);
        Assert.Equal(10, cropResult.Image!.Width);
        Assert.Equal(12, cropResult.Image.Height);
        Assert.True(File.Exists(cropResult.Image.Path));

        Assert.True(resizeResult.Succeeded);
        Assert.NotNull(resizeResult.Image);
        Assert.Equal(20, resizeResult.Image!.Width);
        Assert.Equal(15, resizeResult.Image.Height);
        Assert.True(File.Exists(resizeResult.Image.Path));
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
