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
public sealed class FillBucketTests
{
    [TestMethod]
    public async Task NonContiguousColorSelectionIncludesSeparatedMatchingIslandsWithinTolerance()
    {
        RenderedImage image = Image(7, 3, bitmap =>
        {
            using Graphics graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Black);
            bitmap.SetPixel(0, 1, Color.Red);
            bitmap.SetPixel(5, 1, Color.FromArgb(255, 250, 3, 4));
            bitmap.SetPixel(6, 1, Color.FromArgb(255, 230, 0, 0));
        });
        var editor = new SystemDrawingDocumentRasterEditor();
        RegionFillResult contiguous = await editor.FindFillRegionAsync(new(image, new(0, 1), 5), CancellationToken.None);
        RegionFillResult global = await editor.FindFillRegionAsync(new(image, new(0, 1), 5) { Contiguous = false }, CancellationToken.None);
        Assert.IsTrue(contiguous.Succeeded, contiguous.ErrorMessage);
        Assert.IsTrue(global.Succeeded, global.ErrorMessage);
        Assert.IsFalse(Contains(contiguous.Region!, 5, 1));
        Assert.IsTrue(Contains(global.Region!, 5, 1));
        Assert.IsFalse(Contains(global.Region!, 6, 1));
        Assert.IsFalse(Contains(global.Region!, 3, 1));
    }
    [TestMethod]
    public async Task TransparentCanvas_FillsAsOneCompactRectangleWithoutChangingSource()
    {
        RenderedImage image = Image(128, 96, _ => { });
        byte[] original = image.Pixels.ToArray();
        FillRegion region = await Find(image, new PointD(40, 30));
        Assert.AreEqual(new RectD(0, 0, 128, 96), region.Bounds);
        Assert.AreSequenceEqual(new[] { new RectD(0, 0, 1, 1) }, region.Rectangles);
        Assert.AreEqual(0, region.OriginalColor.A);
        Assert.AreSequenceEqual(original, image.Pixels);
    }

    [TestMethod]
    public async Task ClosedBoundary_SeparatesInsideFromOutsideAndPreservesHoles()
    {
        RenderedImage image = RingImage();
        FillRegion inside = await Find(image, new PointD(5, 5));
        Assert.AreEqual(new RectD(3, 3, 6, 6), inside.Bounds);
        Assert.ContainsSingle(inside.Rectangles);
        FillRegion outside = await Find(image, new PointD(0, 0));
        Assert.IsTrue(Contains(outside, 0, 0));
        Assert.IsTrue(Contains(outside, 11, 11));
        Assert.IsFalse(Contains(outside, 2, 2), "The boundary must be excluded.");
        Assert.IsFalse(Contains(outside, 5, 5), "An enclosed hole must stay unfilled.");
    }

    [TestMethod]
    public async Task OnlyEdgeConnectedPixelsAreFilled()
    {
        RenderedImage image = Image(4, 4, bitmap =>
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            bitmap.SetPixel(0, 0, Color.White);
            bitmap.SetPixel(1, 1, Color.White);
        });
        FillRegion region = await Find(image, new PointD(0, 0), 0);
        Assert.AreEqual(new RectD(0, 0, 1, 1), region.Bounds);
        Assert.IsFalse(Contains(region, 1, 1));
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(16, 2)]
    [DataRow(50, 3)]
    public async Task Tolerance_UsesTheClickedColorRatherThanFollowingAGradient(int tolerance, int expectedWidth)
    {
        RenderedImage image = Image(3, 1, bitmap =>
        {
            bitmap.SetPixel(0, 0, Color.FromArgb(200, 200, 200));
            bitmap.SetPixel(1, 0, Color.FromArgb(210, 210, 210));
            bitmap.SetPixel(2, 0, Color.FromArgb(240, 240, 240));
        });
        FillRegion region = await Find(image, new PointD(0, 0), tolerance);
        Assert.AreEqual(expectedWidth, region.Bounds.Width);
    }

    [TestMethod]
    public async Task TransparentRgb_DoesNotSplitAnInvisibleRegionButAlphaStillBoundsIt()
    {
        RenderedImage image = Image(3, 1, bitmap =>
        {
            bitmap.SetPixel(0, 0, Color.FromArgb(0, 255, 0, 0));
            bitmap.SetPixel(1, 0, Color.FromArgb(0, 0, 0, 255));
            bitmap.SetPixel(2, 0, Color.FromArgb(100, 0, 0, 0));
        });
        FillRegion region = await Find(image, new PointD(0, 0));
        Assert.AreEqual(2, region.Bounds.Width);
    }

    [TestMethod]
    [DataRow(-1.0, 0.0)]
    [DataRow(12.0, 0.0)]
    [DataRow(0.0, double.NaN)]
    public async Task InvalidPoint_ReturnsFailure(double x, double y)
    {
        RegionFillResult result = await new SystemDrawingDocumentRasterEditor().FindFillRegionAsync(
            new RegionFillRequest(RingImage(), new PointD(x, y)), CancellationToken.None);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task Cancellation_StopsBeforeChangingAnything()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => new SystemDrawingDocumentRasterEditor().FindFillRegionAsync(
            new RegionFillRequest(RingImage(), new PointD(0, 0)), cancellation.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Fill_UsesVisibleEditableBoundariesAndSurvivesStorageUndoRedoAndRecolor(bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        string source = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb)) bitmap.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(source, 32, 32, ImagePixelFormat.Bgra32) };
        var border = new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            LayerId = document.Layers[0].Id,
            Bounds = new RectD(4, 4, 24, 24),
            Style = new AnnotationStyle(ColorRgba.Black, ColorRgba.Transparent, ColorRgba.Black, 3, 1)
        };
        document.Annotations.Add(border);
        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        await repository.SaveAsync(document, CancellationToken.None);
        RenderResult rendered = await renderer.RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        FillRegion region = await Find(rendered.Image!, new PointD(16, 16));
        AnnotationObject fill = CreateFill(region, new ColorRgba(255, 0, 0, 255));
        fill.LayerId = document.Layers[0].Id;
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        await editor.AddAsync(document, fill, CancellationToken.None);
        await Check(Color.Red);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSame(border, Assert.ContainsSingle(document.Annotations));
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        await Check(Color.Red);
        await editor.SetStyleAsync(document, fill, fill.Style with { Fill = new ColorRgba(0, 0, 255, 255) }, "Recolor", CancellationToken.None);
        await Check(Color.Blue);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        await Check(Color.Red);
        using var original = new Bitmap(source);
        Assert.AreEqual(0, original.GetPixel(16, 16).A, "Fill must not alter the source image.");

        async Task Check(Color expected)
        {
            await repository.SaveAsync(document, CancellationToken.None);
            CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
            Assert.IsNotNull(loaded);
            Assert.HasCount(2, loaded.Annotations);
            Assert.AreSequenceEqual(fill.FillRectangles, loaded.Annotations[1].FillRectangles);
            Assert.AreEqual(fill.LayerId, loaded.Annotations[1].LayerId);
            RenderResult result = await renderer.RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            using var stream = new MemoryStream(result.Image!.Pixels);
            using var image = new Bitmap(stream);
            Assert.AreEqual(expected.ToArgb(), image.GetPixel(16, 16).ToArgb());
            Assert.AreEqual(0, image.GetPixel(0, 0).A);
            Assert.IsTrue(image.GetPixel(4, 16).A > 200, "The original editable border must stay visible.");
        }
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(2.0)]
    public async Task FilledHolesAndOpacityRemainCorrectWhenScaled(double scale)
    {
        using var workspace = TemporaryWorkspace.Create();
        string source = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(12, 12, PixelFormat.Format32bppArgb)) bitmap.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(source, 12, 12, ImagePixelFormat.Bgra32) };
        AnnotationObject fill = CreateFill(await Find(RingImage(), new PointD(0, 0)), new ColorRgba(255, 0, 0, 255));
        fill.LayerId = document.Layers[0].Id;
        fill.Style = fill.Style with { Opacity = 0.5 };
        document.Annotations.Add(fill);
        var repository = new FileSystemDocumentRepository(workspace.Path);
        await repository.SaveAsync(document, CancellationToken.None);
        RenderResult result = await new SystemDrawingDocumentRenderer(repository).RenderAsync(new RenderRequest(document.Id, scale, null), CancellationToken.None);
        using var stream = new MemoryStream(result.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.AreEqual(0, image.GetPixel((int)(5 * scale), (int)(5 * scale)).A);
        Assert.IsInRange(120, 135, (int)image.GetPixel((int)(1 * scale), (int)(1 * scale)).A);
        Assert.IsInRange(120, 135, (int)image.GetPixel((int)(1 * scale), (int)(3 * scale)).A, "Joined regions must not have darker seams.");
    }

    [TestMethod]
    public async Task FillGeometry_MoveResizeDuplicateAndRasterUndoKeepIndependentRegions()
    {
        var document = new CaptureDocument();
        AnnotationObject fill = CreateFill(await Find(RingImage(), new PointD(5, 5)), ColorRgba.Black);
        fill.LayerId = document.Layers[0].Id;
        document.Annotations.Add(fill);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        RectD before = fill.Bounds;
        await editor.SetBoundsAsync(document, fill, new RectD(10, 20, 30, 40), "Resize Fill", CancellationToken.None);
        Assert.AreEqual(new RectD(10, 20, 30, 40), Assert.ContainsSingle(AreaFill.GetRectangles(fill)));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(before, fill.Bounds);
        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(document, "Copy");
        duplicate.Annotations[0].FillRectangles.Clear();
        Assert.IsNotEmpty(fill.FillRectangles);
        var command = new UpdateDocumentRasterCommand(document.SourceImage, document.SourceImage, [fill], [fill], [], [], "Raster");
        await command.ApplyAsync(document, CancellationToken.None);
        document.Annotations[0].FillRectangles.Clear();
        await command.RevertAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(fill.FillRectangles, document.Annotations[0].FillRectangles);
    }

    [TestMethod]
    public void VersionThreeMigratesAndMalformedFillIsRejected()
    {
        var document = new CaptureDocument { SchemaVersion = 3 };
        Guid layerId = document.Layers[0].Id;
        DocumentLayers.EnsureInitialized(document);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(layerId, document.Layers[0].Id);
        var fill = new AnnotationObject { Kind = AnnotationKind.Fill, LayerId = layerId, Bounds = new RectD(0, 0, 5, 5) };
        document.Annotations.Add(fill);
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        fill.FillRectangles = [new RectD(0, 0, 2, 1)];
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        fill.FillRectangles = [new RectD(double.NaN, 0, 1, 1)];
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
    }

    private static AnnotationObject CreateFill(FillRegion region, ColorRgba color) => new()
    {
        Kind = AnnotationKind.Fill,
        Bounds = region.Bounds,
        FillRectangles = [.. region.Rectangles],
        Style = new AnnotationStyle(ColorRgba.Transparent, color, ColorRgba.Black, 1, 1)
    };

    private static bool Contains(FillRegion region, int x, int y) => AreaFill.GetRectangles(CreateFill(region, ColorRgba.Black))
        .Any(rectangle => x + 0.5 >= rectangle.X && y + 0.5 >= rectangle.Y && x + 0.5 < rectangle.Right && y + 0.5 < rectangle.Bottom);

    private static async Task<FillRegion> Find(RenderedImage image, PointD point, int tolerance = 16)
    {
        RegionFillResult result = await new SystemDrawingDocumentRasterEditor().FindFillRegionAsync(new RegionFillRequest(image, point, tolerance), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        return result.Region!;
    }

    private static RenderedImage RingImage() => Image(12, 12, bitmap =>
    {
        for (int value = 2; value <= 9; value++)
        {
            bitmap.SetPixel(value, 2, Color.Black); bitmap.SetPixel(value, 9, Color.Black);
            bitmap.SetPixel(2, value, Color.Black); bitmap.SetPixel(9, value, Color.Black);
        }
    });

    private static RenderedImage Image(int width, int height, Action<Bitmap> draw)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        draw(bitmap);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new RenderedImage(width, height, stream.ToArray(), "Png");
    }
}
