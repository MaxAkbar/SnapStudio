using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CustomLinePatternTests
{
    private const string Motif = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 8\"><path d=\"M1 1L15 4L1 7Z\" fill=\"#ff0000\"/></svg>";
    private static AnnotationStyle Style => AnnotationStyle.Default with { StrokeThickness = 2, Pattern = StrokePattern.Custom, CustomPattern = new(Motif, "Triangles", Spacing: 1.5) };

    [TestMethod]
    public void RepeatsKeepPhaseAcrossSegmentsRotateAndDoNotDoubleTheClosedSeam()
    {
        var repeats = CustomStrokeGeometry.Place([new([new(0, 0), new(11, 0), new(11, 11)])], 10);
        Assert.HasCount(3, repeats); Assert.AreEqual(new PointD(10, 0), repeats[1].Center);
        Assert.AreEqual(new PointD(11, 9), repeats[2].Center); Assert.AreEqual(90, repeats[2].Angle, .001);
        var closed = CustomStrokeGeometry.Place([new([new(0, 0), new(10, 0), new(10, 10), new(0, 10)], true)], 10);
        Assert.HasCount(4, closed); Assert.AreEqual(4, closed.Select(p => p.Center).Distinct().Count());
        var longPath = CustomStrokeGeometry.Place([new([new(0, 0), new(1_000_000, 0)])], .1);
        Assert.IsTrue(longPath.Count <= CustomStrokeGeometry.MaximumRepeats); Assert.IsTrue(longPath.All(p => double.IsFinite(p.Center.X)));
    }

    [TestMethod]
    public void WidthScaleSpacingAndArrowheadBoundsAffectRealGeometry()
    {
        var path = new[] { new StrokeContour([new(0, 0), new(200, 0)]) };
        var basic = CustomStrokeGeometry.CreateSvg(path, new(Motif, "Triangles"), 2);
        var scaled = CustomStrokeGeometry.CreateSvg(path, new(Motif, "Triangles", Scale: 2), 2);
        var spaced = CustomStrokeGeometry.CreateSvg(path, new(Motif, "Triangles", Spacing: 2), 2);
        XNamespace ns = "http://www.w3.org/2000/svg";
        int Count(string svg) => XElement.Parse(svg).Elements(ns + "use").Count();
        Assert.IsTrue(Count(scaled.Svg) < Count(basic.Svg)); Assert.AreEqual(Count(scaled.Svg), Count(spaced.Svg));
        var arrow = CustomStrokeRenderer.CreateSvg(new() { Kind = AnnotationKind.Arrow, Bounds = new(20, 20, 80, 0), Style = Style with { StrokeThickness = 1 } });
        Assert.IsNotNull(arrow); Assert.IsTrue(arrow.Value.Bounds.Right > 80); Assert.IsTrue(arrow.Value.Svg.Contains("polygon", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(AnnotationKind.Line)] [DataRow(AnnotationKind.Arrow)] [DataRow(AnnotationKind.Rectangle)]
    [DataRow(AnnotationKind.Ellipse)] [DataRow(AnnotationKind.Pen)] [DataRow(AnnotationKind.Vector)]
    public async Task EveryStrokeFamilyRendersSvgMotifsAndRespectsLayerMasks(AnnotationKind kind)
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path);
        var annotation = new AnnotationObject { Kind = kind, LayerId = document.Layers[0].Id, Bounds = new(20, 25, 80, 40), Style = Style,
            StrokePoints = [new(0, 0), new(.3, 1), new(.7, 0), new(1, 1)],
            Vector = kind == AnnotationKind.Vector ? new("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\"><g transform=\"translate(5 5)\"><path d=\"M0 0C30 50 70 50 90 0\" fill=\"none\" stroke=\"blue\" stroke-width=\"2\"/></g></svg>") : null };
        document.Annotations.Add(annotation); document.Layers[0].Mask = new([new(0, 0, 64, 96)]);
        var result = await new SystemDrawingDocumentRenderer(new Repository(document)).RenderAsync(new(document.Id, 1, null), default);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage); using var stream = new MemoryStream(result.Image!.Pixels); using var bitmap = new Bitmap(stream);
        int red = 0;
        for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++)
        {
            var color = bitmap.GetPixel(x, y); if (x >= 64) Assert.AreEqual(0, (int)color.A, $"{kind} mask at {x},{y}");
            if (color.A > 0 && color.R > color.B + 30) red++;
        }
        Assert.IsTrue(red > 10, $"{kind} must render colored SVG motifs, not a solid fallback.");
    }

    [TestMethod]
    public async Task DocumentsLibrariesDuplicationAndUndoRetainEmbeddedArtwork()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = CreateDocument(workspace.Path); var stack = new EditCommandStack();
        var annotation = new AnnotationObject { Kind = AnnotationKind.Line, LayerId = document.Layers[0].Id, Bounds = new(10, 20, 100, 40) }; document.Annotations.Add(annotation);
        await stack.ExecuteAsync(document, new UpdateAnnotationStyleCommand(annotation.Id, annotation.Style, Style), default);
        await stack.UndoAsync(document, default); Assert.AreEqual(StrokePattern.Solid, annotation.Style.Pattern);
        await stack.RedoAsync(document, default); Assert.AreEqual(Motif, annotation.Style.CustomPattern!.Svg);
        var repository = new FileSystemDocumentRepository(Path.Combine(workspace.Path, "documents")); await repository.SaveAsync(document, default);
        var reopened = await repository.GetAsync(document.Id, default); Assert.IsNotNull(reopened); Assert.AreEqual(Motif, reopened.Annotations[0].Style.CustomPattern!.Svg);
        var duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(reopened, "Copy"); Assert.AreEqual(Style.CustomPattern, duplicate.Annotations[0].Style.CustomPattern);
        string path = Path.Combine(workspace.Path, "patterns.json"); var pattern = new ToolDesign(Guid.NewGuid(), "Triangles", AnnotationKind.Line, AnnotationStyle.Default) { Svg = Motif, IsStrokePattern = true };
        await JsonToolDesignStore.WritePackAsync(path, [pattern], default); var patterns = await JsonToolDesignStore.ReadPackAsync(path, default);
        Assert.IsTrue(patterns[0].IsStrokePattern); Assert.AreEqual(SvgShape.Normalize(Motif), patterns[0].Svg);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ToolDesignPack(2, [new(Guid.NewGuid(), "Old preset", AnnotationKind.Line, AnnotationStyle.Default)])));
        Assert.HasCount(1, await JsonToolDesignStore.ReadPackAsync(path, default));
        var old = new CaptureDocument { SchemaVersion = 9 }; DocumentLayers.EnsureInitialized(old); Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, old.SchemaVersion);
    }

    [TestMethod]
    public async Task InvalidPatternsFailBeforeMutatingAnObjectOrItsUndoHistory()
    {
        var document = new CaptureDocument(); var annotation = new AnnotationObject { LayerId = document.Layers[0].Id }; document.Annotations.Add(annotation); var stack = new EditCommandStack();
        var invalid = Style with { CustomPattern = new("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>", "Unsafe") };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await stack.ExecuteAsync(document, new UpdateAnnotationStyleCommand(annotation.Id, annotation.Style, invalid), default));
        Assert.AreEqual(StrokePattern.Solid, annotation.Style.Pattern); Assert.IsFalse(stack.CanUndo);
        Assert.Throws<InvalidDataException>(() => CustomLinePattern.ValidateStyle(Style with { StrokeThickness = 4096 }));
        Assert.Throws<InvalidDataException>(() => CustomLinePattern.ValidateStyle(Style with { CustomPattern = null }));
    }

    [TestMethod]
    public void NativePerspectiveGeometryChangesRebuildPortableSvgRepeats()
    {
        XNamespace ns = "http://www.w3.org/2000/svg";
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CustomLinePattern(Motif, "Triangles"), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var root = new XElement(ns + "svg", new XAttribute("viewBox", "0 0 128 96"),
            new XElement(ns + "path", new XAttribute("id", "curve"), new XAttribute("d", "M20 20 L110 70"), new XAttribute("stroke", "blue"), new XAttribute("stroke-opacity", 0), new XAttribute("fill", "none"),
                new XAttribute("data-snap-stroke", data), new XAttribute("data-snap-stroke-id", "one"),
                new XAttribute("data-snap-stroke-shape", JsonSerializer.Serialize(new { attributes = new { d = "M20 20 L110 20" }, width = 2 }))),
            new XElement(ns + "g", new XAttribute("data-snap-stroke-derived", "one"), new XAttribute("transform", "matrix(1 0 0 1 0 0)")));
        using Bitmap bitmap = SvgRasterizer.Render(root.ToString(), 128, 96);
        int red = 0; for (int y = 35; y < 85; y++) for (int x = 40; x < 125; x++) { var c = bitmap.GetPixel(x, y); if (c.A > 0 && c.R > c.B + 30) red++; }
        Assert.IsTrue(red > 20, "Repeats follow the changed diagonal, rather than the stale horizontal path.");
        Assert.AreEqual(0, (int)bitmap.GetPixel(100, 20).A);
    }

    private static CaptureDocument CreateDocument(string directory)
    {
        string path = Path.Combine(directory, "source.png"); using (var bitmap = new Bitmap(128, 96)) bitmap.Save(path, ImageFormat.Png);
        return new() { SourceImage = new(path, 128, 96, ImagePixelFormat.Bgra32) };
    }
    private sealed class Repository(CaptureDocument document) : IDocumentRepository
    {
        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken token) => Task.FromResult<CaptureDocument?>(document);
        public Task SaveAsync(CaptureDocument document, CancellationToken token) => Task.CompletedTask;
        public Task<bool> DeleteAsync(DocumentId id, CancellationToken token) => Task.FromResult(false);
        public Task<CaptureDocument> CreateFromCaptureAsync(CaptureResult capture, CancellationToken token) => throw new NotSupportedException();
    }
}
