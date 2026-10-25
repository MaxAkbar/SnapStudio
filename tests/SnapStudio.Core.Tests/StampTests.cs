using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class StampTests
{
    private const string SampleSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 100\"><rect width=\"200\" height=\"100\" fill=\"red\"/></svg>";
    private static StampDefinition Custom() => new(Guid.NewGuid(), "Imported", "Custom", SampleSvg);

    [TestMethod]
    public async Task OnlineAttributionSurvivesLibraryAndDocumentReloadsAndDuplication()
    {
        using var workspace = TemporaryWorkspace.Create();
        var attribution = new StampAttribution("Iconify", "tabler:check", "Tabler Icons", "Paweł Kuna", "MIT",
            "https://github.com/tabler/tabler-icons/blob/main/LICENSE", "https://icon-sets.iconify.design/tabler/check/");
        StampDefinition stamp = Custom() with { Attribution = attribution };
        var store = new JsonStampStore(Path.Combine(workspace.Path, "stamps.json"));
        await store.SaveAsync([stamp], CancellationToken.None);
        StampDefinition restored = (await store.LoadAsync(CancellationToken.None)).Single();
        Assert.AreEqual(attribution, restored.Attribution);
        var document = new CaptureDocument();
        AnnotationObject placed = StampCatalog.Place(restored, new(50, 50), new(200, 120), 80);
        placed.LayerId = document.Layers[0].Id;
        document.Annotations.Add(placed);
        var repository = new FileSystemDocumentRepository(Path.Combine(workspace.Path, "documents"));
        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument loaded = (await repository.GetAsync(document.Id, CancellationToken.None))!;
        Assert.AreEqual(attribution, loaded.Annotations.Single().Vector!.Attribution);
        var editor = new DocumentAnnotationEditor(new EditCommandStack());
        await editor.DuplicateAsync(loaded, loaded.Annotations.Single(), CancellationToken.None);
        Assert.HasCount(2, loaded.Annotations);
        Assert.IsTrue(loaded.Annotations.All(item => item.Vector!.Attribution == attribution));
        await store.SaveAsync([], CancellationToken.None);
        Assert.AreEqual(attribution, placed.Vector!.Attribution);
    }

    [TestMethod]
    public void AddingAnOnlineIconDoesNotReuseAnEditedDerivativeWithTheSameSourceId()
    {
        var source = new StampAttribution("Iconify", "tabler:check", "Tabler", "Author", "MIT", "https://example.com/license", "https://example.com/source");
        var icon = new OnlineStampIcon("tabler:check", "check", "Tabler", source);
        StampDefinition original = StampCatalog.Validate(Custom() with { Attribution = source });
        StampDefinition edited = StampCatalog.Validate(original with { Id = Guid.NewGuid(), Name = "Edited check", Svg = SampleSvg.Replace("red", "blue", StringComparison.Ordinal) });
        Assert.IsNull(StampCatalog.FindReusableOnlineStamp([edited], icon, original.Svg));
        Assert.AreEqual(original, StampCatalog.FindReusableOnlineStamp([edited, original], icon, original.Svg));
        Assert.IsNull(StampCatalog.FindReusableOnlineStamp([original], icon with { Attribution = source with { License = "ISC" } }, original.Svg));
    }

    [TestMethod]
    public void AttributionRejectsUnsafeLinksAndOversizedOrMissingMetadata()
    {
        var source = new StampAttribution("Iconify", "tabler:check", "Tabler", "Author", "MIT",
            "https://example.com/license", "https://example.com/source");
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.Validate(Custom() with { Attribution = source with { LicenseUrl = "javascript:alert(1)" } }));
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.Validate(Custom() with { Attribution = source with { SourceUrl = "https://user:secret@example.com/" } }));
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.Validate(Custom() with { Attribution = source with { Author = "" } }));
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.Validate(Custom() with { Attribution = source with { License = new string('x', 121) } }));
    }

    [TestMethod]
    public void EveryBuiltInStampHasUniqueIdentityAndRenderableArtwork()
    {
        Assert.IsTrue(StampCatalog.BuiltIns.Count >= 10);
        StampCatalog.ValidateLibrary(StampCatalog.BuiltIns);
        foreach (StampDefinition stamp in StampCatalog.BuiltIns)
        {
            AnnotationImage preview = SvgRasterizer.Preview(stamp.Svg, 96, 96);
            using var stream = new MemoryStream(preview.PngBytes);
            using var bitmap = new Bitmap(stream);
            bool visible = false;
            for (int y = 0; y < bitmap.Height && !visible; y++)
                for (int x = 0; x < bitmap.Width && !visible; x++) visible = bitmap.GetPixel(x, y).A > 0;
            Assert.IsTrue(visible, stamp.Name + " must have visible artwork.");
        }
    }

    [TestMethod]
    public void PlacementPreservesAspectRatioAndKeepsArtworkInsideTheImage()
    {
        StampDefinition stamp = Custom();
        AnnotationObject topLeft = StampCatalog.Place(stamp, new(0, 0), new(100, 60), 80);
        AnnotationObject bottomRight = StampCatalog.Place(stamp, new(100, 60), new(100, 60), 80);
        AnnotationObject oversized = StampCatalog.Place(stamp, new(500, 500), new(100, 60), 400);
        Assert.AreEqual(new RectD(0, 0, 80, 40), topLeft.Bounds);
        Assert.AreEqual(new RectD(20, 20, 80, 40), bottomRight.Bounds);
        Assert.AreEqual(new RectD(0, 10, 100, 50), oversized.Bounds);
        Assert.AreEqual(AnnotationKind.Vector, topLeft.Kind);
        Assert.AreEqual(stamp.Name, topLeft.Vector!.Name);
        Assert.AreNotEqual(topLeft.Id, bottomRight.Id);
        Assert.ThrowsExactly<ArgumentException>(() => StampCatalog.Place(stamp, new(0, 0), new(100, 60), double.NaN));
    }

    [TestMethod]
    public void ImportAcceptsCommonStandaloneIconSvgAndRejectsExternalResources()
    {
        const string icon = "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M4 12 L10 18 L20 6\"/></svg>";
        StampDefinition stamp = StampCatalog.Validate(Custom() with { Svg = icon });
        Assert.Contains("viewBox=\"0 0 24 24\"", stamp.Svg);
        Assert.IsTrue(SvgRasterizer.Preview(stamp.Svg, 48, 48).PngBytes.Length > 0);
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.Validate(stamp with
        { Svg = "<svg viewBox=\"0 0 24 24\"><use href=\"https://example.com/remote.svg#icon\"/></svg>" }));
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.Validate(stamp with { Name = " " }));
    }

    [TestMethod]
    public async Task CustomLibrarySurvivesRestartAndRemovingItDoesNotAlterPlacedCopies()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "stamps.json");
        StampDefinition stamp = Custom() with { Name = "  Wide icon  ", Category = "  Reviews  " };
        await new JsonStampStore(path).SaveAsync([stamp], CancellationToken.None);
        IReadOnlyList<StampDefinition> reloaded = await new JsonStampStore(path).LoadAsync(CancellationToken.None);
        Assert.ContainsSingle(reloaded);
        Assert.AreEqual("Wide icon", reloaded[0].Name);
        Assert.AreEqual("Reviews", reloaded[0].Category);
        AnnotationObject placed = StampCatalog.Place(reloaded[0], new(100, 100), new(320, 200), 96);
        string svg = placed.Vector!.Svg;
        await new JsonStampStore(path).SaveAsync([], CancellationToken.None);
        Assert.IsEmpty(await new JsonStampStore(path).LoadAsync(CancellationToken.None));
        Assert.AreEqual(svg, placed.Vector.Svg);
        Assert.IsFalse(placed.Vector.Svg.Contains(path, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task InvalidAndCancelledSavesPreserveTheExistingLibrary()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "stamps.json");
        var store = new JsonStampStore(path);
        StampDefinition stamp = Custom();
        await store.SaveAsync([stamp], CancellationToken.None);
        string before = await File.ReadAllTextAsync(path);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.SaveAsync([stamp with { Name = "" }], CancellationToken.None));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync([stamp], cancelled.Token));
        Assert.AreEqual(before, await File.ReadAllTextAsync(path));
        Assert.IsEmpty(Directory.GetFiles(workspace.Path, "*.tmp"));
    }

    [TestMethod]
    public async Task FutureLibraryVersionsAndDuplicateNamesAreRejected()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "stamps.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":2,\"stamps\":[]}");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new JsonStampStore(path).LoadAsync(CancellationToken.None));
        StampDefinition stamp = Custom();
        Assert.ThrowsExactly<InvalidDataException>(() => StampCatalog.ValidateLibrary([stamp, stamp with { Id = Guid.NewGuid(), Name = stamp.Name.ToUpperInvariant() }]));
        Assert.HasCount(2, StampCatalog.ValidateLibrary([stamp, stamp with { Id = Guid.NewGuid(), Category = "Reviews" }]));
    }

    [TestMethod]
    public async Task RenderedStampsKeepTheirPositionColorAndTransparencyInImageAndSelectionOutput()
    {
        using var workspace = TemporaryWorkspace.Create();
        string source = Path.Combine(workspace.Path, "transparent.png");
        using (var blank = new Bitmap(200, 120)) blank.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new(source, 200, 120, ImagePixelFormat.Rgba32) };
        StampDefinition circle = Custom() with
        {
            Svg = "<svg viewBox=\"0 0 100 100\"><circle cx=\"50\" cy=\"50\" r=\"35\" fill=\"blue\"/></svg>"
        };
        AnnotationObject red = StampCatalog.Place(Custom(), new(50, 30), new(200, 120), 80);
        AnnotationObject blue = StampCatalog.Place(circle, new(140, 80), new(200, 120), 40);
        red.LayerId = blue.LayerId = document.Layers[0].Id;
        document.Annotations.AddRange([red, blue]);
        var repository = new FileSystemDocumentRepository(Path.Combine(workspace.Path, "documents"));
        await repository.SaveAsync(document, CancellationToken.None);
        var renderer = new SystemDrawingDocumentRenderer(repository);
        var result = await renderer.RenderAsync(new(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        using var stream = new MemoryStream(result.Image!.Pixels);
        using var image = new Bitmap(stream);
        Assert.AreEqual(Color.Red.ToArgb(), image.GetPixel(20, 20).ToArgb());
        Assert.AreEqual(Color.Blue.ToArgb(), image.GetPixel(140, 80).ToArgb());
        Assert.AreEqual(0, image.GetPixel(121, 61).A);
        Assert.AreEqual(0, image.GetPixel(1, 1).A);
        var cropped = await renderer.RenderAsync(new(document.Id, 1, red.Bounds), CancellationToken.None);
        Assert.IsTrue(cropped.Succeeded, cropped.ErrorMessage);
        using var cropStream = new MemoryStream(cropped.Image!.Pixels);
        using var crop = new Bitmap(cropStream);
        Assert.AreEqual(new Size(80, 40), crop.Size);
        Assert.AreEqual(Color.Red.ToArgb(), crop.GetPixel(40, 20).ToArgb());
    }

    [TestMethod]
    public async Task PlacingAStampUsesOneUndoStepAndEmbedsTheSvgInTheSavedDocument()
    {
        using var workspace = TemporaryWorkspace.Create();
        var document = new CaptureDocument();
        AnnotationObject placed = StampCatalog.Place(Custom(), new(50, 50), new(320, 200), 96);
        placed.LayerId = document.Layers[0].Id;
        var stack = new EditCommandStack();
        await new DocumentAnnotationEditor(stack).AddAsync(document, placed, CancellationToken.None);
        var repository = new FileSystemDocumentRepository(workspace.Path);
        await repository.SaveAsync(document, CancellationToken.None);
        CaptureDocument? reloaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.AreEqual(placed.Vector, reloaded!.Annotations.Single().Vector);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.IsEmpty(document.Annotations);
        Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual(placed.Id, document.Annotations.Single().Id);
    }
}
