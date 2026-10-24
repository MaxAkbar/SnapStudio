using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class TextAnnotationLayoutTests
{
    [TestMethod]
    [DataRow(12)]
    [DataRow(24)]
    [DataRow(144)]
    public async Task SelectedTextRenderContainsCompleteTextAtEnteredSize(int fontSize)
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string source = Path.Combine(workspace.Path, "transparent.png");
        using (var bitmap = new Bitmap(1200, 600, PixelFormat.Format32bppArgb)) bitmap.Save(source, ImageFormat.Png);
        var document = new CaptureDocument { SourceImage = new ImageAsset(source, 1200, 600, ImagePixelFormat.Bgra32) };
        var text = new AnnotationObject
        {
            Kind = AnnotationKind.Text, Text = "Wgyp", LayerId = document.Layers[0].Id,
            Style = AnnotationStyle.Default with { StrokeThickness = fontSize },
            Bounds = TextAnnotationLayout.FitBounds(new RectD(40, 30, 700, 400), "Wgyp", fontSize)
        };
        document.Annotations.Add(text);
        var renderer = new SystemDrawingDocumentRenderer(new Repository(document));

        RenderResult selected = await renderer.RenderAsync(new RenderRequest(document.Id, 1, text.Bounds), CancellationToken.None);
        RenderResult whole = await renderer.RenderAsync(new RenderRequest(document.Id, 1, null), CancellationToken.None);
        Assert.IsTrue(selected.Succeeded, selected.ErrorMessage);
        using Bitmap selection = Decode(selected.Image!.Pixels);
        using Bitmap full = Decode(whole.Image!.Pixels);
        int selectedPixels = 0, fullPixels = 0;
        int firstRow = selection.Height, lastRow = -1;
        for (int y = 0; y < full.Height; y++)
            for (int x = 0; x < full.Width; x++)
                if (full.GetPixel(x, y).A > 0) fullPixels++;
        for (int y = 0; y < selection.Height; y++)
            for (int x = 0; x < selection.Width; x++)
                if (selection.GetPixel(x, y).A > 0) { selectedPixels++; firstRow = Math.Min(firstRow, y); lastRow = Math.Max(lastRow, y); }

        Assert.IsTrue(selectedPixels > fontSize, "Copying selected text produced an empty image.");
        Assert.AreEqual(fullPixels, selectedPixels, "The selection clipped some text pixels.");
        Assert.IsTrue(lastRow - firstRow < fontSize * 1.5, "The renderer enlarged text beyond its entered size.");
        Assert.IsTrue(text.Bounds.Width < 700 && text.Bounds.Height < 400, "The box kept the drawn rectangle instead of fitting the text.");
        Assert.AreEqual(0, selection.GetPixel(0, 0).A);
    }

    [TestMethod]
    public void ResizeScalesTextAndKeepsTheOppositeCornerAnchored()
    {
        var text = new AnnotationObject { Kind = AnnotationKind.Text, Text = "Resize text", Style = AnnotationStyle.Default with { StrokeThickness = 80 } };
        text.Bounds = TextAnnotationLayout.FitBounds(new RectD(10, 20, 0, 0), text.Text, 80);
        RectD before = text.Bounds;
        RectD requested = new(before.X + before.Width / 2, before.Y + before.Height / 2, before.Width / 2, before.Height / 2);

        (RectD bounds, AnnotationStyle style) = TextAnnotationLayout.Resize(text, requested);

        Assert.AreEqual(40, style.StrokeThickness);
        Assert.AreEqual(before.Right, bounds.Right, 0.001);
        Assert.AreEqual(before.Bottom, bounds.Bottom, 0.001);
        SizeD measured = TextAnnotationLayout.Measure(text.Text, 40);
        Assert.AreEqual(measured.Width, bounds.Width);
        Assert.AreEqual(measured.Height, bounds.Height);
        Assert.AreEqual(before, text.Bounds, "Resize preview changed the document.");
        Assert.AreEqual(80, text.Style.StrokeThickness);
    }

    [TestMethod]
    public async Task TextAndStyleEditsRestoreTheirFittedBoundsWithOneUndo()
    {
        var document = new CaptureDocument();
        var text = new AnnotationObject { Kind = AnnotationKind.Text, Text = "Before", LayerId = document.Layers[0].Id,
            Bounds = new RectD(10, 20, 90, 30), Style = AnnotationStyle.Default with { StrokeThickness = 24 } };
        document.Annotations.Add(text);
        var stack = new EditCommandStack();
        var editor = new DocumentAnnotationEditor(stack);
        RectD original = text.Bounds;
        RectD changedTextBounds = TextAnnotationLayout.FitBounds(original, "Longer text", 24);
        await editor.SetTextAsync(document, text, "Longer text", CancellationToken.None, changedTextBounds);
        Assert.AreEqual(changedTextBounds, text.Bounds);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreEqual("Before", text.Text);
        Assert.AreEqual(original, text.Bounds);
        await stack.RedoAsync(document, CancellationToken.None);
        Assert.AreEqual("Longer text", text.Text);
        Assert.AreEqual(changedTextBounds, text.Bounds);

        AnnotationStyle style = text.Style with { StrokeThickness = 144 };
        RectD resized = TextAnnotationLayout.FitBounds(text.Bounds, text.Text, 144);
        await editor.SetStyleAsync(document, text, style, "Resize Text", CancellationToken.None, resized);
        Assert.AreEqual(resized, text.Bounds);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreEqual(24, text.Style.StrokeThickness);
        Assert.AreEqual(changedTextBounds, text.Bounds);
        await stack.RedoAsync(document, CancellationToken.None);
        Assert.AreEqual(144, text.Style.StrokeThickness);
        Assert.AreEqual(resized, text.Bounds);
    }

    [TestMethod]
    public async Task GroupStyleRestoresTextBoundsAndOtherObjectsTogether()
    {
        var document = new CaptureDocument();
        var text = new AnnotationObject { Kind = AnnotationKind.Text, Text = "Group", LayerId = document.Layers[0].Id, Bounds = new RectD(10, 20, 100, 30) };
        var rectangle = new AnnotationObject { Kind = AnnotationKind.Rectangle, LayerId = document.Layers[0].Id, Bounds = new RectD(200, 30, 100, 100) };
        document.Annotations.AddRange([text, rectangle]);
        var stack = new EditCommandStack();
        RectD before = text.Bounds;
        RectD fitted = TextAnnotationLayout.FitBounds(before, text.Text, 120);
        await new DocumentSelectionEditor(stack).SetStylesAsync(document,
            new Dictionary<Guid, AnnotationStyle> { [text.Id] = text.Style with { StrokeThickness = 120 }, [rectangle.Id] = rectangle.Style with { StrokeThickness = 120 } },
            CancellationToken.None, new Dictionary<Guid, RectD> { [text.Id] = fitted });
        Assert.AreEqual(fitted, text.Bounds);
        Assert.AreEqual(120, rectangle.Style.StrokeThickness);
        await stack.UndoAsync(document, CancellationToken.None);
        Assert.AreEqual(before, text.Bounds);
        Assert.AreEqual(2, text.Style.StrokeThickness);
        Assert.AreEqual(2, rectangle.Style.StrokeThickness);
    }

    private static Bitmap Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        return new Bitmap(bitmap);
    }

    private sealed class Repository(CaptureDocument document) : IDocumentRepository
    {
        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken token) => Task.FromResult<CaptureDocument?>(document);
        public Task SaveAsync(CaptureDocument document, CancellationToken token) => Task.CompletedTask;
        public Task<CaptureDocument> CreateFromCaptureAsync(CaptureResult capture, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(DocumentId id, CancellationToken token) => throw new NotSupportedException();
    }
}
