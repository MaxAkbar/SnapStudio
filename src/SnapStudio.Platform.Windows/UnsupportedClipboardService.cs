using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedClipboardService : IClipboardService
{
    public Task<ClipboardResult> CopyDocumentAsync(
        DocumentId documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ClipboardResult.Failed(
            "Clipboard integration is deferred until the still-capture vertical slice."));
    }

    public Task<ClipboardResult> CopyTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ClipboardResult.Failed(
            "Clipboard text integration is unavailable in this session."));
    }
}
