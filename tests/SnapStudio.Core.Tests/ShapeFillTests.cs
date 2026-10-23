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
public sealed class ShapeFillTests
{
    [TestMethod]
    public void Target_UsesLayerAndObjectPaintOrderAndSkipsHiddenShapes()
    {
        var document = new CaptureDocument();
        var upperLayer = new AnnotationLayer();
        document.Layers.Add(upperLayer);
        AnnotationObject lower = Shape(document, AnnotationKind.Rectangle);
        AnnotationObject upper = Shape(document, AnnotationKind.Ellipse);
        upper.LayerId = upperLayer.Id;
        AnnotationObject lastInLowerLayer = Shape(document, AnnotationKind.Rectangle);
        document.Annotations = [lower, upper, lastInLowerLayer];
        var point = new PointD(16, 16);

        Assert.AreSame(upper, ShapeFill.FindTarget(document, point));
        upper.IsVisible = false;
        Assert.AreSame(lastInLowerLayer, ShapeFill.FindTarget(document, point));
        upper.IsVisible = true;
        upperLayer.IsVisible = false;
        Assert.AreSame(lastInLowerLayer, ShapeFill.FindTarget(document, point));
        lastInLowerLayer.IsVisible = false;
        Assert.AreSame(lower, ShapeFill.FindTarget(document, point));
        document.Layers[0].IsVisible = false;
        Assert.IsNull(ShapeFill.FindTarget(document, point));
    }

    [TestMethod]
    public void Target_UsesActualEllipseAndRoundedRectangleInteriors()
    {
        var document = new CaptureDocument();
        AnnotationObject lower = Shape(document, AnnotationKind.Rectangle);
        AnnotationObject upper = Shape(document, AnnotationKind.Ellipse);
        document.Annotations = [lower, upper];

        Assert.AreSame(upper, ShapeFill.FindTarget(document, new PointD(16, 16)));
        Assert.AreSame(upper, ShapeFill.FindTarget(document, new PointD(16, 4)));
        Assert.AreSame(lower, ShapeFill.FindTarget(document, new PointD(5, 5)), "An ellipse's bounding-box corner is outside its interior.");
        upper.Kind = AnnotationKind.Rectangle;
        upper.Style = upper.Style with { CornerRadius = 8 };
        Assert.AreSame(lower, ShapeFill.FindTarget(document, new PointD(5, 5)));
        Assert.AreSame(upper, ShapeFill.FindTarget(document, new PointD(8, 8)));
        upper.Style = upper.Style with { CornerRadius = 200 };
        Assert.AreSame(upper, ShapeFill.FindTarget(document, new PointD(16, 4)), "Large corner radii must clamp to half the smaller dimension.");
        Assert.AreSame(lower, ShapeFill.FindTarget(document, new PointD(5, 5)));
    }

    [TestMethod]
    public void Target_ReturnsNoShapeForImageAreasOtherToolsOrInvalidGeometry()
    {
        var document = new CaptureDocument();
        AnnotationObject shape = Shape(document, AnnotationKind.Rectangle);
        document.Annotations.Add(shape);
        Assert.IsNull(ShapeFill.FindTarget(document, new PointD(0, 0)));
        Assert.IsNull(ShapeFill.FindTarget(document, new PointD(double.NaN, 16)));
        Assert.IsNull(ShapeFill.FindTarget(document, new PointD(16, double.PositiveInfinity)));
        foreach (AnnotationKind kind in Enum.GetValues<AnnotationKind>().Except([AnnotationKind.Rectangle, AnnotationKind.Ellipse]))
        {
            shape.Kind = kind;
            Assert.IsNull(ShapeFill.FindTarget(document, new PointD(16, 16)));
        }

        shape.Kind = AnnotationKind.Ellipse;
        shape.Bounds = new RectD(4, 4, 0, 24);
        Assert.IsNull(ShapeFill.FindTarget(document, new PointD(4, 16)));
        shape.Bounds = new RectD(4, 4, double.NaN, 24);
        Assert.IsNull(ShapeFill.FindTarget(document, new PointD(16, 16)));
    }

    [TestMethod]
    [DataRow(AnnotationKind.Rectangle, false)]
    [DataRow(AnnotationKind.Rectangle, true)]
    [DataRow(AnnotationKind.Ellipse, false)]
    [DataRow(AnnotationKind.Ellipse, true)]
    public async Task Fill_StaysWithOriginalShapeThroughUndoMovementStorageAndRendering(AnnotationKind kind, bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        string source = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb)) bitmap.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(source, 64, 64, ImagePixelFormat.Bgra32) };
        AnnotationObject shape = Shape(document, kind);
        document.Annotations.Add(shape);
        Guid id = shape.Id;
        AnnotationStyle originalStyle = shape.Style;
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        AnnotationObject target = ShapeFill.FindTarget(document, new PointD(16, 16))!;

        Assert.IsTrue(await editor.SetShapeFillAsync(document, target, new ColorRgba(255, 0, 0, 255), 1, CancellationToken.None));
        Assert.ContainsSingle(document.Annotations, "Filling an editable shape must not add a separate region object.");
        Assert.AreSame(shape, document.Annotations[0]);
        Assert.AreEqual(originalStyle with { Fill = new ColorRgba(255, 0, 0, 255) }, shape.Style);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(originalStyle, shape.Style);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        RectD movedBounds = new(32, 32, 28, 28);
        await editor.SetBoundsAsync(document, shape, movedBounds, "Move and resize", CancellationToken.None);

        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument loaded = (await repository.GetAsync(document.Id, CancellationToken.None))!;
        AnnotationObject saved = Assert.ContainsSingle(loaded.Annotations);
        Assert.AreEqual(id, saved.Id);
        Assert.AreEqual(shape.LayerId, saved.LayerId);
        Assert.AreEqual(kind, saved.Kind);
        Assert.AreEqual(movedBounds, saved.Bounds);
        Assert.AreEqual(shape.Style, saved.Style);

        RenderResult rendered = await new SystemDrawingDocumentRenderer(repository).RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(rendered.Succeeded, rendered.ErrorMessage);
        using var stream = new MemoryStream(rendered.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.AreEqual(Color.Red.ToArgb(), image.GetPixel(46, 46).ToArgb());
        Assert.AreEqual(0, image.GetPixel(16, 16).A, "The fill must leave the old position with the shape.");
        Assert.AreEqual(Color.Black.ToArgb(), image.GetPixel(32, 46).ToArgb(), "The outline must keep its color.");
        if (kind == AnnotationKind.Ellipse)
        {
            Assert.AreEqual(0, image.GetPixel(33, 33).A, "An ellipse fill must not occupy the corners of its bounds.");
        }

        using var originalImage = new Bitmap(source);
        Assert.AreEqual(0, originalImage.GetPixel(46, 46).A, "Object fills must leave the source PNG unchanged.");
    }

    [TestMethod]
    public async Task FillOpacity_ChangesOnlyInteriorAndRepeatedColorAddsNoUndoStep()
    {
        var document = new CaptureDocument();
        AnnotationObject shape = Shape(document, AnnotationKind.Rectangle);
        shape.Style = shape.Style with { Opacity = 0.7, CornerRadius = 6 };
        AnnotationStyle before = shape.Style;
        document.Annotations.Add(shape);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        var color = new ColorRgba(10, 20, 30, 200);

        Assert.IsTrue(await editor.SetShapeFillAsync(document, shape, color, 0.5, CancellationToken.None));
        Assert.AreEqual(before with { Fill = new ColorRgba(10, 20, 30, 100) }, shape.Style);
        Assert.IsFalse(await editor.SetShapeFillAsync(document, shape, color, 0.5, CancellationToken.None));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(before, shape.Style);
        Assert.IsFalse(stack.CanUndo, "A repeated fill must not insert an extra undo step.");
    }

    [TestMethod]
    public async Task Fill_RejectsStaleObjectsOtherToolsAndCancellationBeforeMutation()
    {
        var document = new CaptureDocument();
        AnnotationObject shape = Shape(document, AnnotationKind.Rectangle);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await editor.SetShapeFillAsync(document, shape, ColorRgba.Black, 1, CancellationToken.None));
        document.Annotations.Add(shape);
        shape.Kind = AnnotationKind.Line;
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await editor.SetShapeFillAsync(document, shape, ColorRgba.Black, 1, CancellationToken.None));
        shape.Kind = AnnotationKind.Rectangle;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await editor.SetShapeFillAsync(document, shape, ColorRgba.Black, 1, cancellation.Token));
        Assert.IsFalse(stack.CanUndo);
        Assert.AreEqual(ColorRgba.Transparent, shape.Style.Fill);
    }

    private static AnnotationObject Shape(CaptureDocument document, AnnotationKind kind) => new()
    {
        Kind = kind,
        LayerId = document.Layers[0].Id,
        Bounds = new RectD(4, 4, 24, 24),
        Style = new AnnotationStyle(ColorRgba.Black, ColorRgba.Transparent, ColorRgba.Black, 3, 1)
    };
}
