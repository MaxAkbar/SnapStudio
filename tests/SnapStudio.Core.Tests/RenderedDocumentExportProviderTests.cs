using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class RenderedDocumentExportProviderTests
{
    [TestMethod]
    public async Task ExportAsync_WhenFormatIsPng_WritesRenderedPng()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string outputPath = Path.Combine(workspace.Path, "export.png");
        byte[] renderedBytes = CreateTinyPngBytes();
        var provider = new RenderedDocumentExportProvider(
            ExportFormat.Png,
            new FakeDocumentRenderer(RenderResult.Success(new RenderedImage(1, 1, renderedBytes, "Png"))));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(DocumentId.New(), ExportFormat.Png, outputPath, new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreSequenceEqual(renderedBytes, await File.ReadAllBytesAsync(outputPath));
    }

    [TestMethod]
    public async Task ExportAsync_WhenRendererFails_ReturnsFailure()
    {
        var provider = new RenderedDocumentExportProvider(
            ExportFormat.Png,
            new FakeDocumentRenderer(RenderResult.Failed("render failed")));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(DocumentId.New(), ExportFormat.Png, "export.png", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("render failed", result.ErrorMessage);
    }

    private static byte[] CreateTinyPngBytes()
    {
        return
        [
            137, 80, 78, 71, 13, 10, 26, 10,
            0, 0, 0, 13, 73, 72, 68, 82,
            0, 0, 0, 1, 0, 0, 0, 1,
            8, 6, 0, 0, 0, 31, 21, 196,
            137, 0, 0, 0, 13, 73, 68, 65,
            84, 120, 156, 99, 248, 207, 192,
            240, 31, 0, 5, 0, 1, 255, 137,
            153, 61, 29, 0, 0, 0, 0, 73,
            69, 78, 68, 174, 66, 96, 130
        ];
    }

    private sealed class FakeDocumentRenderer(RenderResult result) : IDocumentRenderer
    {
        public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
