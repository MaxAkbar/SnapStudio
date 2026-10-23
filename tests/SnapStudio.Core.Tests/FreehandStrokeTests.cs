using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Platform.Windows;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class FreehandStrokeTests
{
    [TestMethod]
    public void Create_PreservesTheCurveAndOwnsItsSamples()
    {
        var samples = new List<PointD> { new(5, 5), new(18, 24), new(30, 9) };
        AnnotationObject stroke = FreehandStroke.Create(samples, AnnotationStyle.Default);
        Assert.AreEqual(AnnotationKind.Pen, stroke.Kind);
        Assert.AreEqual(new RectD(5, 5, 25, 19), stroke.Bounds);
        Assert.AreSequenceEqual(samples, FreehandStroke.GetPoints(stroke));
        samples.Clear();
        Assert.HasCount(3, stroke.StrokePoints);
        FreehandStroke.Validate(stroke);
    }

    [TestMethod]
    public async Task Stroke_AddMoveResizeAndRemoveAreUndoable()
    {
        var document = new CaptureDocument();
        AnnotationObject stroke = FreehandStroke.Create([new(4, 6), new(14, 26), new(24, 6)], AnnotationStyle.Default);
        stroke.LayerId = document.Layers[0].Id;
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        await editor.AddAsync(document, stroke, CancellationToken.None);
        Assert.ContainsSingle(document.Annotations);
        await editor.SetBoundsAsync(document, stroke, new RectD(10, 20, 40, 60), "Resize Pen", CancellationToken.None);
        Assert.AreSequenceEqual(new[] { new PointD(10, 20), new PointD(30, 80), new PointD(50, 20) }, FreehandStroke.GetPoints(stroke));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(new RectD(4, 6, 20, 20), stroke.Bounds);
        await editor.RemoveAsync(document, stroke, CancellationToken.None);
        Assert.IsEmpty(document.Annotations);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSame(stroke, document.Annotations[0]);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.IsEmpty(document.Annotations);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stroke_SurvivesStorageAndExportsTheCurveWithTransparency(bool useDatabase)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        IDocumentRepository repository = useDatabase
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        CaptureDocument document = CreateDocument(workspace.Path, [new(8, 8), new(16, 32), new(40, 8)]);
        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);
        AnnotationObject stroke = Assert.ContainsSingle(loaded.Annotations);
        Assert.AreSequenceEqual(document.Annotations[0].StrokePoints, stroke.StrokePoints);
        Assert.AreEqual(document.Layers[0].Id, stroke.LayerId);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        var exporter = new RenderedDocumentExportProvider(ExportFormat.Png, renderer);
        string path = Path.Combine(workspace.Path, "pen.png");
        ExportResult result = await exporter.ExportAsync(new ExportRequest(document.Id, ExportFormat.Png, path, new Dictionary<string, string>()), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var image = new Bitmap(path);
        Assert.IsTrue(image.GetPixel(16, 31).R > 200);
        Assert.IsTrue(image.GetPixel(16, 31).A > 200);
        Assert.AreEqual(0, image.GetPixel(24, 8).A, "The curve must not turn into a line between its endpoints.");
        Assert.AreEqual(0, image.GetPixel(50, 50).A);
    }

    [TestMethod]
    public async Task Stroke_RenderingRespectsZoomAndViewport()
    {
        using var workspace = TemporaryWorkspace.Create();
        CaptureDocument document = CreateDocument(workspace.Path, [new(8, 8), new(16, 32), new(40, 8)]);
        var repository = new FileSystemDocumentRepository(workspace.Path);
        await repository.SaveAsync(document, CancellationToken.None);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        RenderResult result = await renderer.RenderAsync(new RenderRequest(document.Id, 2, new RectD(4, 4, 48, 48)), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var stream = new MemoryStream(result.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.AreEqual(96, image.Width);
        Assert.IsTrue(image.GetPixel(24, 55).A > 200);
        Assert.AreEqual(0, image.GetPixel(80, 80).A);
    }

    [TestMethod]
    public async Task Stroke_SinglePointDrawsARoundDotAndRetainsOpacity()
    {
        using var workspace = TemporaryWorkspace.Create();
        CaptureDocument document = CreateDocument(workspace.Path, [new(20, 20)]);
        document.Annotations[0].Style = document.Annotations[0].Style with { StrokeThickness = 8, Opacity = 0.5 };
        var repository = new FileSystemDocumentRepository(workspace.Path);
        await repository.SaveAsync(document, CancellationToken.None);
        RenderResult result = await new SystemDrawingDocumentRenderer(repository).RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var stream = new MemoryStream(result.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.IsInRange(120, 135, (int)image.GetPixel(20, 20).A);
        Assert.AreEqual(0, image.GetPixel(25, 25).A);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Stroke_AxisAlignedLinesHaveValidBounds(bool horizontal)
    {
        PointD[] points = horizontal ? [new(2, 10), new(20, 10)] : [new(10, 2), new(10, 20)];
        AnnotationObject stroke = FreehandStroke.Create(points, AnnotationStyle.Default);
        FreehandStroke.Validate(stroke);
        Assert.AreSequenceEqual(points, FreehandStroke.GetPoints(stroke));
    }

    [TestMethod]
    public void Stroke_RejectsEmptyOrNonfiniteSamplesAndMalformedSavedData()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FreehandStroke.Create([], AnnotationStyle.Default));
        Assert.ThrowsExactly<ArgumentException>(() => FreehandStroke.Create([new(double.NaN, 0)], AnnotationStyle.Default));
        AnnotationObject stroke = FreehandStroke.Create([new(4, 4)], AnnotationStyle.Default);
        var document = new CaptureDocument();
        stroke.LayerId = document.Layers[0].Id;
        document.Annotations.Add(stroke);
        stroke.StrokePoints = [new(2, 0)];
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        stroke.StrokePoints = [];
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
    }

    [TestMethod]
    public void Duplicate_DeepCopiesPenSamples()
    {
        var document = new CaptureDocument();
        AnnotationObject stroke = FreehandStroke.Create([new(1, 2), new(3, 4)], AnnotationStyle.Default);
        stroke.LayerId = document.Layers[0].Id;
        document.Annotations.Add(stroke);
        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(document, "Copy");
        Assert.AreSequenceEqual(stroke.StrokePoints, duplicate.Annotations[0].StrokePoints);
        duplicate.Annotations[0].StrokePoints[0] = new PointD(0.5, 0.5);
        Assert.AreEqual(new PointD(0, 0), stroke.StrokePoints[0]);
    }

    [TestMethod]
    public async Task RasterUndoRedo_PreservesIndependentStrokeSamples()
    {
        var document = new CaptureDocument();
        AnnotationObject before = FreehandStroke.Create([new(2, 2), new(12, 12)], AnnotationStyle.Default);
        before.LayerId = document.Layers[0].Id;
        AnnotationObject after = FreehandStroke.Create([new(4, 4), new(24, 24)], AnnotationStyle.Default);
        after.LayerId = before.LayerId;
        var command = new UpdateDocumentRasterCommand(document.SourceImage, document.SourceImage,
            [before], [after], [], [], "Resize");
        await command.ApplyAsync(document, CancellationToken.None);
        document.Annotations[0].StrokePoints.Clear();
        await command.RevertAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(before.StrokePoints, document.Annotations[0].StrokePoints);
        await command.ApplyAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(after.StrokePoints, document.Annotations[0].StrokePoints);
    }

    [TestMethod]
    public void VersionTwoDocument_UpgradesWithoutChangingExistingLayerOrAnnotation()
    {
        var document = new CaptureDocument { SchemaVersion = 2 };
        var annotation = new AnnotationObject { LayerId = document.Layers[0].Id };
        document.Annotations.Add(annotation);
        Guid layer = document.Layers[0].Id;
        DocumentLayers.EnsureInitialized(document);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(layer, document.Layers[0].Id);
        Assert.AreSame(annotation, document.Annotations[0]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CurrentVersion_DoesNotRepairMissingLayersOrAssignments(bool hasAnnotation)
    {
        var document = new CaptureDocument { Layers = [] };
        if (hasAnnotation) document.Annotations.Add(new AnnotationObject());
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.EnsureInitialized(document));
        Assert.IsEmpty(document.Layers);
        if (hasAnnotation) Assert.IsNull(document.Annotations[0].LayerId);
    }

    private static CaptureDocument CreateDocument(string path, IReadOnlyList<PointD> points)
    {
        string source = Path.Combine(path, "source.png");
        using (var image = new Bitmap(64, 64, PixelFormat.Format32bppArgb)) image.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(source, 64, 64, ImagePixelFormat.Bgra32) };
        AnnotationObject stroke = FreehandStroke.Create(points,
            new AnnotationStyle(new ColorRgba(255, 0, 0, 255), ColorRgba.Transparent, ColorRgba.Black, 4, 1));
        stroke.LayerId = document.Layers[0].Id;
        document.Annotations.Add(stroke);
        return document;
    }
}
