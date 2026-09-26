using Microsoft.Graphics.Canvas;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;

namespace SnapStudio.Platform.Windows;

public sealed class SourceImageExportProvider : IExportProvider
{
    private readonly IDocumentRepository _documentRepository;

    public SourceImageExportProvider(
        ExportFormat format,
        IDocumentRepository documentRepository)
    {
        ArgumentNullException.ThrowIfNull(documentRepository);

        Format = format;
        _documentRepository = documentRepository;
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

        CaptureDocument? document;
        try
        {
            document = await _documentRepository
                .GetAsync(request.DocumentId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return ExportResult.Failed(exception.Message);
        }

        if (document is null)
        {
            return ExportResult.Failed("The selected document could not be found.");
        }

        if (!File.Exists(document.SourceImage.Path))
        {
            return ExportResult.Failed("The source image for the selected document could not be found.");
        }

        string? outputDirectory = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (Format == ExportFormat.Png)
        {
            File.Copy(document.SourceImage.Path, request.OutputPath, overwrite: true);
            return ExportResult.Success(request.OutputPath);
        }

        CanvasDevice canvasDevice = CanvasDevice.GetSharedDevice();
        using CanvasBitmap bitmap = await CanvasBitmap
            .LoadAsync(canvasDevice, document.SourceImage.Path)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        await bitmap
            .SaveAsync(request.OutputPath, CanvasBitmapFileFormat.Jpeg)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return ExportResult.Success(request.OutputPath);
    }
}
