using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsClipboardServiceTests
{
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

    private sealed class FakeDocumentRenderer(RenderResult result) : IDocumentRenderer
    {
        public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
