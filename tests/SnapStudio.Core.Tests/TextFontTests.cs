using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class TextFontTests
{
    [TestMethod]
    public void InstalledFontsAreDistinctSortedAndUsableByTheRenderer()
    {
        IReadOnlyList<string> fonts = SystemFontCatalog.Families;
        Assert.IsTrue(fonts.Count > 0);
        Assert.AreEqual(fonts.Count, fonts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreSequenceEqual(fonts.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase), fonts);
        foreach (string family in fonts)
        {
            using Font font = SystemFontCatalog.CreateFont(family, 24);
            Assert.AreEqual(family, font.FontFamily.Name, ignoreCase: true, "An installed family silently fell back to another font.");
        }
    }

    [TestMethod]
    public void UnavailableFontUsesTheSameInstalledFallbackForMeasurementAndRendering()
    {
        const string missing = "SnapStudio Uninstalled Test Font 65289";
        string fallback = SystemFontCatalog.ResolveFamily(missing);
        Assert.Contains(fallback, SystemFontCatalog.Families);
        using Font font = SystemFontCatalog.CreateFont(missing, 48);
        Assert.AreEqual(fallback, font.FontFamily.Name);
        Assert.AreEqual(TextAnnotationLayout.Measure("Fallback text", 48, fallback), TextAnnotationLayout.Measure("Fallback text", 48, missing));
    }

    [TestMethod]
    public void LegacyDocumentWithoutFontMigratesToTheDefaultFont()
    {
        CaptureDocument document = CreateDocument();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonNode json = JsonSerializer.SerializeToNode(document, options)!;
        json["schemaVersion"] = 5;
        json["annotations"]![0]!["style"]!.AsObject().Remove("fontFamily");

        CaptureDocument loaded = JsonSerializer.Deserialize<CaptureDocument>(json, options)!;
        DocumentLayers.EnsureInitialized(loaded);

        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(AnnotationStyle.DefaultFontFamily, loaded.Annotations[0].Style.FontFamily);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChosenFontSurvivesSavingAndReopening(bool database)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        IDocumentRepository repository = database
            ? new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path))
            : new FileSystemDocumentRepository(workspace.Path);
        CaptureDocument document = CreateDocument();
        document.Annotations[0].Style = document.Annotations[0].Style with { FontFamily = "Consolas" };

        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Consolas", loaded.Annotations[0].Style.FontFamily);
        Assert.AreEqual(document.Annotations[0].Text, loaded.Annotations[0].Text);
    }

    [TestMethod]
    public async Task FontChangeRefitsTextAndUndoesFontAndBoundsTogether()
    {
        CaptureDocument document = CreateDocument();
        AnnotationObject text = document.Annotations[0];
        text.Bounds = TextAnnotationLayout.FitBounds(text.Bounds, text.Text, 48, text.Style.FontFamily);
        RectD before = text.Bounds;
        var stack = new EditCommandStack();
        AnnotationStyle changed = text.Style with { FontFamily = "Consolas" };
        RectD fitted = TextAnnotationLayout.FitBounds(text.Bounds, text.Text, 48, changed.FontFamily);

        await new DocumentAnnotationEditor(stack).SetStyleAsync(document, text, changed, "Change Font", CancellationToken.None, fitted);
        Assert.AreEqual("Consolas", text.Style.FontFamily);
        Assert.AreEqual(48, text.Style.StrokeThickness);
        Assert.AreNotEqual(before.Width, text.Bounds.Width);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreEqual(AnnotationStyle.DefaultFontFamily, text.Style.FontFamily);
        Assert.AreEqual(before, text.Bounds);
        await stack.RedoAsync(document, CancellationToken.None);
        Assert.AreEqual("Consolas", text.Style.FontFamily);
        Assert.AreEqual(fitted, text.Bounds);
    }

    [TestMethod]
    public async Task CopyAndExportRendererUsesChosenFontWithTransparentSelection()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string source = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(1200, 600, PixelFormat.Format32bppArgb)) bitmap.Save(source, ImageFormat.Png);
        CaptureDocument document = CreateDocument();
        document.SourceImage = new ImageAsset(source, 1200, 600, ImagePixelFormat.Bgra32);
        AnnotationObject text = document.Annotations[0];
        var renderer = new SystemDrawingDocumentRenderer(new Repository(document));
        var outputs = new List<RenderedImage>();
        foreach (string family in new[] { AnnotationStyle.DefaultFontFamily, "Consolas" })
        {
            text.Style = text.Style with { FontFamily = family };
            text.Bounds = TextAnnotationLayout.FitBounds(text.Bounds, text.Text, 48, family);
            RenderResult result = await renderer.RenderAsync(new RenderRequest(document.Id, 1, text.Bounds), CancellationToken.None);
            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            outputs.Add(result.Image!);
            using var stream = new MemoryStream(result.Image!.Pixels);
            using var bitmap = new Bitmap(stream);
            Assert.AreEqual(0, bitmap.GetPixel(0, 0).A);
            Assert.IsTrue(Enumerable.Range(0, bitmap.Width).Any(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).A > 0)));
        }
        Assert.AreNotEqual(outputs[0].Width, outputs[1].Width, "The font did not change rendered text metrics.");
        Assert.IsFalse(outputs[0].Pixels.SequenceEqual(outputs[1].Pixels), "Copy/export ignored the chosen font.");
    }

    private static CaptureDocument CreateDocument()
    {
        var document = new CaptureDocument();
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Text,
            Text = "iiiiiiiiii",
            LayerId = document.Layers[0].Id,
            Bounds = new RectD(20, 20, 400, 100),
            Style = AnnotationStyle.Default with { StrokeThickness = 48 }
        });
        return document;
    }

    private sealed class Repository(CaptureDocument document) : IDocumentRepository
    {
        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken token) => Task.FromResult<CaptureDocument?>(document);
        public Task SaveAsync(CaptureDocument document, CancellationToken token) => Task.CompletedTask;
        public Task<CaptureDocument> CreateFromCaptureAsync(CaptureResult capture, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(DocumentId id, CancellationToken token) => throw new NotSupportedException();
    }
}
