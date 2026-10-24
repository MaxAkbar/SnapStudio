using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class SvgToolTests
{
    private const string Artwork = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' preserveAspectRatio='none'><defs><linearGradient id='paint'><stop stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><clipPath id='clip'><circle cx='50' cy='50' r='35'/></clipPath></defs><g clip-path='url(#clip)' transform='translate(0 0)'><path d='M0 0H100V100H0Z' fill='url(#paint)'/></g></svg>";

    [TestMethod]
    public void Normalize_PreservesVectorArtworkAndDerivesPixelViewBox()
    {
        string normalized = SvgShape.Normalize(Artwork);
        XElement root = XElement.Parse(normalized);
        Assert.AreEqual("0 0 100 100", (string?)root.Attribute("viewBox"));
        Assert.AreEqual(1, root.Descendants().Count(item => item.Name.LocalName == "path"));
        Assert.AreEqual(2, root.Descendants().Count(item => item.Name.LocalName == "stop"));
        Assert.AreEqual(normalized, SvgShape.Normalize(normalized));
        Assert.AreEqual("0 0 300 200", (string?)XElement.Parse(SvgShape.Normalize("<svg width='300px' height='200'><rect width='300' height='200'/></svg>")).Attribute("viewBox"));
        Assert.IsNotNull(SvgShape.Normalize(SvgToolTemplates.Blank, allowEmpty: true));
        Assert.ThrowsExactly<InvalidDataException>(() => SvgShape.Normalize(SvgToolTemplates.Blank));
        string text = SvgShape.Normalize("<svg><text><tspan>One</tspan> <tspan>two</tspan></text></svg>");
        Assert.AreEqual("One two", XElement.Parse(text, LoadOptions.PreserveWhitespace).Descendants().Single(item => item.Name.LocalName == "text").Value);
    }

    [TestMethod]
    [DataRow("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///test'>]><svg><text>&x;</text></svg>")]
    [DataRow("<svg><script>alert(1)</script><rect/></svg>")]
    [DataRow("<svg><foreignObject/><rect/></svg>")]
    [DataRow("<svg><rect onclick='bad()'/></svg>")]
    [DataRow("<svg><use href='https://example.com/file.svg#shape'/></svg>")]
    [DataRow("<svg><rect fill='url(https://example.com/paint)'/></svg>")]
    [DataRow("<svg><style>@import 'https://example.com/a.css';</style><rect/></svg>")]
    [DataRow("<svg><g id='a'><use href='#a'/></g></svg>")]
    [DataRow("<svg><defs><pattern id='a'><rect fill='url(#a)'/></pattern></defs><rect fill='url(#a)'/></svg>")]
    [DataRow("<svg><rect fill='url(#missing)'/></svg>")]
    [DataRow("<svg viewBox='0 0 0 100'><rect/></svg>")]
    public void Normalize_RejectsUnsupportedOrInvalidSvg(string source) => Assert.ThrowsExactly<InvalidDataException>(() => SvgShape.Normalize(source));

    [TestMethod]
    public void Templates_StartNewArtworkEmptyAndRetainExistingToolAppearance()
    {
        Assert.AreEqual(SvgToolTemplates.Blank, SvgToolTemplates.Create(AnnotationKind.Vector, AnnotationStyle.Default));
        foreach (AnnotationKind kind in new[] { AnnotationKind.Rectangle, AnnotationKind.Ellipse, AnnotationKind.Line, AnnotationKind.Arrow, AnnotationKind.Text, AnnotationKind.Pen, AnnotationKind.Highlight, AnnotationKind.Fill })
            Assert.IsNotNull(SvgShape.Normalize(SvgToolTemplates.Create(kind, AnnotationStyle.Default with { StrokeThickness = 50, Pattern = StrokePattern.Dashed })));
    }

    [TestMethod]
    public async Task Designs_RoundTripSvgAndReadLegacyLibrary()
    {
        using var folder = TemporaryWorkspace.Create();
        string path = Path.Combine(folder.Path, "designs.json");
        var design = ToolDesigns.Validate(new(Guid.NewGuid(), "Gradient badge", AnnotationKind.Rectangle, AnnotationStyle.Default) { Svg = Artwork });
        var store = new JsonToolDesignStore(path);
        await store.SaveAsync([design], CancellationToken.None);
        Assert.AreEqual(design, (await store.LoadAsync(CancellationToken.None)).Single());
        string saved = File.ReadAllText(path);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.SaveAsync([design with { Svg = "<svg/>" }], CancellationToken.None));
        Assert.AreEqual(saved, File.ReadAllText(path));
        File.WriteAllText(path, "{\"schemaVersion\":1,\"designs\":[]}");
        Assert.IsEmpty(await store.LoadAsync(CancellationToken.None));
        Assert.ThrowsExactly<InvalidDataException>(() => ToolDesigns.Validate(design with { Kind = AnnotationKind.Vector, Svg = null }));
    }

    [TestMethod]
    public async Task Documents_StoreSvgInBothBackendsAndDuplicateIndependently()
    {
        using var files = TemporaryWorkspace.Create(); using var database = CSharpDbTestWorkspace.Create();
        foreach (IDocumentRepository repository in new IDocumentRepository[] { new FileSystemDocumentRepository(files.Path), new CSharpDbDocumentRepository(new(database.Path)) })
        {
            var document = new CaptureDocument { SchemaVersion = 7 };
            document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Vector, LayerId = document.Layers[0].Id, Bounds = new(10, 20, 160, 90), Vector = new(Artwork, "Badge") });
            DocumentLayers.EnsureInitialized(document);
            await repository.SaveAsync(document, CancellationToken.None);
            CaptureDocument loaded = (await repository.GetAsync(document.Id, CancellationToken.None))!;
            Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(document.Annotations[0].Vector, loaded.Annotations[0].Vector);
            CaptureDocument copy = CaptureDocumentWorkspaceEditor.CreateDuplicate(loaded, "Independent SVG");
            Assert.AreEqual(loaded.Annotations[0].Vector, copy.Annotations[0].Vector);
            var stack = new EditCommandStack(); var editor = new DocumentSelectionEditor(stack);
            var objects = await editor.DuplicateAsync(loaded, [loaded.Annotations[0].Id], CancellationToken.None);
            Assert.AreEqual(loaded.Annotations[0].Vector, objects.Single().Vector);
            copy.Annotations[0].Vector = copy.Annotations[0].Vector! with { Svg = SvgToolTemplates.Create(AnnotationKind.Ellipse, AnnotationStyle.Default) };
            Assert.AreEqual(Artwork, loaded.Annotations[0].Vector!.Svg);
        }
    }

    [TestMethod]
    public async Task EditingSvg_UndoesAndRedoesOnlyTheArtwork()
    {
        var document = new CaptureDocument(); var stack = new EditCommandStack();
        var item = new AnnotationObject { Kind = AnnotationKind.Vector, LayerId = document.Layers[0].Id, Bounds = new(10, 20, 160, 90), Vector = new(Artwork) };
        document.Annotations.Add(item); AnnotationVector original = item.Vector, after = original with { Svg = SvgToolTemplates.Create(AnnotationKind.Rectangle, AnnotationStyle.Default) };
        await stack.ExecuteAsync(document, new UpdateAnnotationVectorCommand(item.Id, original, after), CancellationToken.None);
        Assert.AreEqual(after, item.Vector);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None)); Assert.AreEqual(original, item.Vector);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None)); Assert.AreEqual(after, item.Vector);
        Assert.AreEqual(new RectD(10, 20, 160, 90), item.Bounds);
    }

    [TestMethod]
    public async Task Render_UsesSvgGeometryGradientsTransparencyAndObjectOpacity()
    {
        using Bitmap small = SvgRasterizer.Render(Artwork, 100, 100), large = SvgRasterizer.Render(Artwork, 200, 100);
        Assert.AreEqual(0, small.GetPixel(1, 1).A);
        Assert.AreEqual(255, small.GetPixel(50, 50).A);
        Assert.IsTrue(small.GetPixel(25, 50).R > small.GetPixel(25, 50).B);
        Assert.IsTrue(small.GetPixel(75, 50).B > small.GetPixel(75, 50).R);
        Assert.AreEqual(255, large.GetPixel(150, 50).A);
        Assert.AreEqual(0, large.GetPixel(3, 50).A);
        Assert.AreSame(SvgRasterizer.Preview(Artwork, 100, 100), SvgRasterizer.Preview(Artwork, 100, 100));
        using var folder = TemporaryWorkspace.Create(); string source = Path.Combine(folder.Path, "blank.png");
        using (var bitmap = new Bitmap(220, 140)) bitmap.Save(source, ImageFormat.Png);
        var repository = new FileSystemDocumentRepository(Path.Combine(folder.Path, "documents"));
        var document = new CaptureDocument { SourceImage = new(source, 220, 140, SnapStudio.Core.Capture.ImagePixelFormat.Rgba32) };
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Vector, LayerId = document.Layers[0].Id, Bounds = new(20, 20, 180, 100), Vector = new(Artwork), Style = AnnotationStyle.Default with { Opacity = 0.5 } });
        await repository.SaveAsync(document, CancellationToken.None);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        RenderResult result = await renderer.RenderAsync(new(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var stream = new MemoryStream(result.Image!.Pixels); using var rendered = new Bitmap(stream);
        Assert.AreEqual(0, rendered.GetPixel(21, 21).A);
        Assert.IsTrue(rendered.GetPixel(110, 70).A is >= 125 and <= 130);
    }
}
