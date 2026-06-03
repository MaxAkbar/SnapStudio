using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Export;

public interface IExportProvider
{
    ExportFormat Format { get; }

    Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken cancellationToken);
}

public interface IExportDestinationPicker
{
    Task<ExportDestination?> PickDestinationAsync(
        ExportFormat format,
        string suggestedFileName,
        CancellationToken cancellationToken);
}

public interface IClipboardService
{
    Task<ClipboardResult> CopyDocumentAsync(DocumentId documentId, CancellationToken cancellationToken);

    Task<ClipboardResult> CopyTextAsync(string text, CancellationToken cancellationToken);
}
