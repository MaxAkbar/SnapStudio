using SnapStudio.Core.Export;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsClipboardServiceTests
{
    [TestMethod]
    public async Task EmptyPixelSelectionCannotFallBackToCopyingTheWholeImage()
    {
        var renderer = new FakeDocumentRenderer(RenderResult.Failed("Should not render."));
        var clipboard = new WindowsClipboardService(renderer);
        ClipboardResult result = await clipboard.CopySelectionAsync(DocumentId.New(), PixelSelection.Empty(20, 20), CancellationToken.None);
        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(renderer.LastRequest);
    }
    [TestMethod]
    public async Task CopySelectionPassesTheMaskAndItsBoundingRectangleToTheRenderer()
    {
        var renderer = new FakeDocumentRenderer(RenderResult.Failed("Controlled render failure."));
        var clipboard = new WindowsClipboardService(renderer);
        PixelSelection selection = PixelSelection.Create(20, 20, PixelSelectionTool.Ellipse, new(3, 4, 10, 12));
        await clipboard.CopySelectionAsync(DocumentId.New(), selection, CancellationToken.None);
        Assert.AreSame(selection, renderer.LastRequest!.Selection);
        Assert.AreEqual(selection.Bounds, renderer.LastRequest.Viewport);
        Assert.IsFalse(selection.Contains(3, 4));
    }
    [TestMethod]
    public async Task CopyDocumentAsync_WhenDocumentIsMissing_ReturnsFailure()
    {
        var clipboard = new WindowsClipboardService(
            new FakeDocumentRenderer(RenderResult.Failed("The selected document could not be found.")));

        ClipboardResult result = await clipboard.CopyDocumentAsync(
            DocumentId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.Contains("could not be found", result.ErrorMessage ?? string.Empty);
    }

    [TestMethod]
    public async Task CopyDocumentAsync_WhenRenderFails_ReturnsFailure()
    {
        var clipboard = new WindowsClipboardService(
            new FakeDocumentRenderer(RenderResult.Failed("The source image for the selected document could not be found.")));

        ClipboardResult result = await clipboard.CopyDocumentAsync(
            DocumentId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.Contains("source image", result.ErrorMessage ?? string.Empty);
    }

    [TestMethod]
    public async Task CopyDocumentAsync_SelectionRendersExactSourceAreaAtOriginalResolution()
    {
        var renderer = new FakeDocumentRenderer(RenderResult.Failed("Render unavailable."));
        var clipboard = new WindowsClipboardService(renderer);
        DocumentId id = DocumentId.New();
        var bounds = new RectD(20.5, 10.25, 40, 30);

        ClipboardResult result = await clipboard.CopyDocumentAsync(id, bounds, CancellationToken.None);

        Assert.AreEqual(new RenderRequest(id, 1, bounds), renderer.LastRequest);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Render unavailable.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task CopyDocumentAsync_WholeImageHasNoSelectionViewport()
    {
        var renderer = new FakeDocumentRenderer(RenderResult.Failed("Render unavailable."));
        var clipboard = new WindowsClipboardService(renderer);
        DocumentId id = DocumentId.New();

        await clipboard.CopyDocumentAsync(id, CancellationToken.None);

        Assert.AreEqual(new RenderRequest(id, 1, null), renderer.LastRequest);
    }

    [TestMethod]
    public async Task CopyDocumentAsync_RenderExceptionReturnsFailureInsteadOfAbortingCopy()
    {
        var clipboard = new WindowsClipboardService(new ThrowingRenderer());
        ClipboardResult result = await clipboard.CopyDocumentAsync(DocumentId.New(), new RectD(0, 0, 30, 20), CancellationToken.None);
        Assert.IsFalse(result.Succeeded);
        Assert.Contains("render error", result.ErrorMessage ?? "");
    }

    private sealed class ThrowingRenderer : IDocumentRenderer
    {
        public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Controlled render error");
    }

    private sealed class FakeDocumentRenderer(RenderResult result) : IDocumentRenderer
    {
        public RenderRequest? LastRequest { get; private set; }

        public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
