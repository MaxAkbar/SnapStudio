using System.Text;
using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class PdfDocumentExportProviderTests
{
    [Fact]
    public async Task ExportAsync_WhenFormatIsPdf_WritesPdfDocument()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string outputPath = Path.Combine(workspace.Path, "export.pdf");
        var provider = new PdfDocumentExportProvider(
            new FakeDocumentRenderer(RenderResult.Success(
                new RenderedImage(1, 1, CreateTinyPngBytes(), "Png"))));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(DocumentId.New(), ExportFormat.Pdf, outputPath, new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(outputPath, result.OutputPath);

        byte[] pdfBytes = await File.ReadAllBytesAsync(outputPath);
        string pdfText = Encoding.Latin1.GetString(pdfBytes);
        Assert.StartsWith("%PDF-1.4", pdfText);
        Assert.Contains("/Subtype /Image", pdfText);
        Assert.Contains("/Filter /DCTDecode", pdfText);
        Assert.Contains("startxref", pdfText);
        Assert.EndsWith("%%EOF\n", pdfText);
    }

    [Fact]
    public async Task ExportAsync_WhenRendererFails_ReturnsFailure()
    {
        var provider = new PdfDocumentExportProvider(
            new FakeDocumentRenderer(RenderResult.Failed("render failed")));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(DocumentId.New(), ExportFormat.Pdf, "export.pdf", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("render failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ExportAsync_WhenFormatDoesNotMatchProvider_ReturnsFailure()
    {
        var provider = new PdfDocumentExportProvider(
            new FakeDocumentRenderer(RenderResult.Failed("render should not be called")));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(DocumentId.New(), ExportFormat.Png, "export.png", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot export", result.ErrorMessage);
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
