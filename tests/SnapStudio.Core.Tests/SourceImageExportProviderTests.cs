using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class SourceImageExportProviderTests
{
    [Fact]
    public async Task ExportAsync_WhenFormatIsPng_CopiesSourceImage()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        string outputPath = Path.Combine(workspace.Path, "export.png");
        byte[] expected = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(sourcePath, expected);

        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset(sourcePath, 10, 10, ImagePixelFormat.Bgra32)
        };
        var provider = new SourceImageExportProvider(
            ExportFormat.Png,
            new FakeDocumentRepository(document));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(document.Id, ExportFormat.Png, outputPath, new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.Equal(expected, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task ExportAsync_WhenDocumentIsMissing_ReturnsFailure()
    {
        var provider = new SourceImageExportProvider(
            ExportFormat.Png,
            new FakeDocumentRepository(document: null));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(
                DocumentId.New(),
                ExportFormat.Png,
                "export.png",
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be found", result.ErrorMessage);
    }

    [Fact]
    public async Task ExportAsync_WhenFormatDoesNotMatchProvider_ReturnsFailure()
    {
        var provider = new SourceImageExportProvider(
            ExportFormat.Png,
            new FakeDocumentRepository(document: null));

        ExportResult result = await provider.ExportAsync(
            new ExportRequest(
                DocumentId.New(),
                ExportFormat.Jpeg,
                "export.jpg",
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot export", result.ErrorMessage);
    }

    private sealed class FakeDocumentRepository(CaptureDocument? document) : IDocumentRepository
    {
        public Task<CaptureDocument> CreateFromCaptureAsync(
            CaptureResult capture,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken cancellationToken)
        {
            return Task.FromResult(document?.Id == id ? document : null);
        }

        public Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(DocumentId id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
