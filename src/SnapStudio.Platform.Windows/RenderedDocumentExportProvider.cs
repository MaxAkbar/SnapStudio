using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Export;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Platform.Windows;

public sealed class RenderedDocumentExportProvider : IExportProvider
{
    private readonly IDocumentRenderer _documentRenderer;

    public RenderedDocumentExportProvider(
        ExportFormat format,
        IDocumentRenderer documentRenderer)
    {
        ArgumentNullException.ThrowIfNull(documentRenderer);

        Format = format;
        _documentRenderer = documentRenderer;
    }

    public ExportFormat Format { get; }

    public async Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Format != Format)
        {
            return ExportResult.Failed(
                $"The {Format} export provider cannot export {request.Format}.");
        }

        if (Format is not ExportFormat.Png and not ExportFormat.Jpeg)
        {
            return ExportResult.Failed($"{Format} export is not implemented.");
        }

        RenderResult renderResult = await _documentRenderer
            .RenderAsync(new RenderRequest(request.DocumentId, 1, null), cancellationToken)
            .ConfigureAwait(false);

        if (!renderResult.Succeeded || renderResult.Image is not RenderedImage image)
        {
            return ExportResult.Failed(renderResult.ErrorMessage ?? "The document could not be rendered.");
        }

        string? outputDirectory = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (Format == ExportFormat.Png)
        {
            await File
                .WriteAllBytesAsync(request.OutputPath, image.Pixels, cancellationToken)
                .ConfigureAwait(false);
            return ExportResult.Success(request.OutputPath);
        }

        await SaveJpegAsync(image.Pixels, request.OutputPath, cancellationToken)
            .ConfigureAwait(false);
        return ExportResult.Success(request.OutputPath);
    }

    private static Task SaveJpegAsync(
        byte[] pngBytes,
        string outputPath,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var input = new MemoryStream(pngBytes);
                using var image = Image.FromStream(input);
                using var output = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
                using (Graphics graphics = Graphics.FromImage(output))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImageUnscaled(image, 0, 0);
                }

                output.Save(outputPath, ImageFormat.Jpeg);
            },
            cancellationToken);
    }
}
