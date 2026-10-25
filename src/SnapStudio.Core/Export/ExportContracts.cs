using SnapStudio.Core.Primitives;
using SnapStudio.Core.Documents;

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

    Task<ClipboardResult> CopyDocumentAsync(DocumentId documentId, RectD? sourceBounds, CancellationToken cancellationToken);

    Task<ClipboardResult> CopySelectionAsync(DocumentId documentId, PixelSelection selection, CancellationToken cancellationToken);

    Task<ClipboardResult> CopyTextAsync(string text, CancellationToken cancellationToken);
}
