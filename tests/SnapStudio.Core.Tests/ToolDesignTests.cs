using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ToolDesignTests
{
    private static ToolDesign Design(StrokePattern pattern = StrokePattern.Dashed) =>
        new(Guid.NewGuid(), "Dashed rectangle", AnnotationKind.Rectangle, AnnotationStyle.Default with { Pattern = pattern, StrokeThickness = 3 });

    [TestMethod]
    public async Task Library_RoundTripsAndRejectsInvalidUpdatesWithoutOverwriting()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "designs.json");
        var store = new JsonToolDesignStore(path);
        Assert.IsEmpty(await store.LoadAsync(CancellationToken.None));
        ToolDesign design = Design();
        await store.SaveAsync([design], CancellationToken.None);
        Assert.AreEqual(design, (await new JsonToolDesignStore(path).LoadAsync(CancellationToken.None)).Single());
        string before = File.ReadAllText(path);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.SaveAsync([design with { Name = "" }], CancellationToken.None));
        Assert.AreEqual(before, File.ReadAllText(path));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync([Design(StrokePattern.Dotted)], cancelled.Token));
        Assert.AreEqual(before, File.ReadAllText(path));
        Assert.IsEmpty(Directory.GetFiles(workspace.Path, "*.tmp"));
    }

    [TestMethod]
    public async Task Import_RejectsFutureVersionsAndMalformedData()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "designs.json");
        foreach (string json in new[] { JsonSerializer.Serialize(new { schemaVersion = ToolDesignPack.CurrentSchemaVersion + 1, designs = Array.Empty<ToolDesign>() }), "{\"schemaVersion\":1,\"designs\":null}" })
        {
            File.WriteAllText(path, json);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => JsonToolDesignStore.ReadPackAsync(path, CancellationToken.None));
        }
        File.WriteAllText(path, "broken json");
        await Assert.ThrowsAsync<JsonException>(() => JsonToolDesignStore.ReadPackAsync(path, CancellationToken.None));
    }

    [TestMethod]
    public void Validation_RejectsUnsupportedKindsPatternsAndDuplicateNames()
    {
        ToolDesign design = Design();
        Assert.ThrowsExactly<InvalidDataException>(() => ToolDesigns.Validate(design with { Kind = AnnotationKind.Image }));
        Assert.ThrowsExactly<InvalidDataException>(() => ToolDesigns.Validate(design with { Kind = AnnotationKind.Text }));
        Assert.ThrowsExactly<InvalidDataException>(() => ToolDesigns.Validate(design with { Style = design.Style with { StrokeThickness = double.NaN } }));
        Assert.ThrowsExactly<InvalidDataException>(() => ToolDesigns.ValidateLibrary([design, design with { Id = Guid.NewGuid(), Name = design.Name.ToUpperInvariant() }]));
        Assert.ThrowsExactly<InvalidDataException>(() => ToolDesigns.ValidateLibrary([design, design with { Name = "Other" }]));
        Assert.AreEqual(800, ToolDesigns.Validate(design with { Style = design.Style with { StrokeThickness = 800 } }).Style.StrokeThickness);
    }

    [TestMethod]
    public void LegacyStyles_DefaultToSolidAndVersionSixDocumentsMigrate()
    {
        var document = new CaptureDocument { SchemaVersion = 6 };
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle, LayerId = document.Layers[0].Id });
        DocumentLayers.EnsureInitialized(document);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(StrokePattern.Solid, document.Annotations[0].Style.Pattern);
        string json = JsonSerializer.Serialize(AnnotationStyle.Default);
        using JsonDocument parsed = JsonDocument.Parse(json);
        var legacy = parsed.RootElement.EnumerateObject().Where(property => property.Name != "Pattern")
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        Assert.AreEqual(StrokePattern.Solid, JsonSerializer.Deserialize<AnnotationStyle>(JsonSerializer.Serialize(legacy))!.Pattern);
    }

    [TestMethod]
    public async Task Documents_RetainPatternsAcrossDatabaseAndFileStorageAndDuplication()
    {
        using var files = TemporaryWorkspace.Create(); using var database = CSharpDbTestWorkspace.Create();
        foreach (IDocumentRepository repository in new IDocumentRepository[] { new FileSystemDocumentRepository(files.Path), new CSharpDbDocumentRepository(new(database.Path)) })
        {
            var document = new CaptureDocument();
            document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle, LayerId = document.Layers[0].Id, Style = Design().Style });
            await repository.SaveAsync(document, CancellationToken.None);
            CaptureDocument loaded = (await repository.GetAsync(document.Id, CancellationToken.None))!;
            Assert.AreEqual(StrokePattern.Dashed, loaded.Annotations[0].Style.Pattern);
            CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(loaded, "Independent style");
            duplicate.Annotations[0].Style = duplicate.Annotations[0].Style with { Pattern = StrokePattern.Dotted };
            Assert.AreEqual(StrokePattern.Dashed, loaded.Annotations[0].Style.Pattern);
        }
    }

    [TestMethod]
    [DataRow(AnnotationKind.Rectangle)]
    [DataRow(AnnotationKind.Ellipse)]
    [DataRow(AnnotationKind.Line)]
    [DataRow(AnnotationKind.Arrow)]
    [DataRow(AnnotationKind.Pen)]
    public async Task Render_StrokedToolsHaveVisiblePatternGaps(AnnotationKind kind)
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "source.png");
        using (var source = new Bitmap(210, 130)) { using Graphics graphics = Graphics.FromImage(source); graphics.Clear(Color.White); source.Save(path, ImageFormat.Png); }
        var repository = new FileSystemDocumentRepository(Path.Combine(workspace.Path, "documents"));
        var document = new CaptureDocument { SourceImage = new(path, 210, 130, SnapStudio.Core.Capture.ImagePixelFormat.Rgba32) };
        var shape = new AnnotationObject
        {
            Kind = kind,
            LayerId = document.Layers[0].Id,
            Bounds = new(20, 20, 160, kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse ? 80 : 1),
            StrokePoints = kind == AnnotationKind.Pen ? [new(0, 0), new(1, 0)] : [],
            Style = Design(StrokePattern.Solid).Style
        };
        document.Annotations.Add(shape);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        async Task<int> Ink(StrokePattern pattern)
        {
            shape.Style = shape.Style with { Pattern = pattern };
            await repository.SaveAsync(document, CancellationToken.None);
            RenderResult result = await renderer.RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
            Assert.IsTrue(result.Succeeded);
            using var stream = new MemoryStream(result.Image!.Pixels); using var bitmap = new Bitmap(stream);
            if (kind == AnnotationKind.Ellipse)
                return Enumerable.Range(0, 360).Count(angle => { double radians = angle * Math.PI / 180; return bitmap.GetPixel((int)Math.Round(100 + 80 * Math.Cos(radians)), (int)Math.Round(60 + 40 * Math.Sin(radians))).R < 128; });
            return Enumerable.Range(25, 100).Count(x => bitmap.GetPixel(x, 20).R < 128);
        }
        int solid = await Ink(StrokePattern.Solid);
        foreach (StrokePattern pattern in new[] { StrokePattern.Dashed, StrokePattern.Dotted, StrokePattern.DashDot })
        {
            int patterned = await Ink(pattern);
            Assert.IsTrue(patterned > 0 && patterned < solid, $"{kind} / {pattern} did not render visible ink and gaps ({patterned}/{solid}).");
        }
    }
}
