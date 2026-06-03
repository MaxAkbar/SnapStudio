using SnapStudio.Core.Export;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedExportProvider : IExportProvider
{
    public UnsupportedExportProvider(ExportFormat format)
    {
        Format = format;
    }

    public ExportFormat Format { get; }

    public Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ExportResult.Failed(
            $"{Format} export is deferred until the still-capture vertical slice."));
    }
}
