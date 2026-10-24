using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    public bool CanEditCurrentCaptureTags => HasCurrentDocument && CanManageCaptureHistory;
    public string CurrentCaptureTagsLabel => _currentDocument is null || DocumentTags.Read(_currentDocument).Length == 0
        ? "No tags" : string.Join("  ", DocumentTags.Read(_currentDocument).Select(tag => $"#{tag}"));

    public IReadOnlyList<string> GetCaptureTags(string documentId) =>
        _currentDocumentId?.ToString() == documentId && _currentDocument is not null ? DocumentTags.Read(_currentDocument)
            : _allRecentDocuments.FirstOrDefault(document => document.Id.ToString() == documentId)?.Tags ?? [];

    public IReadOnlyList<string> AvailableCaptureTags => _allRecentDocuments.SelectMany(document => document.Tags)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public Task SetCaptureTagsAsync(string documentId, IEnumerable<string> tags, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(documentId, out Guid value)) return Task.CompletedTask;
        string[] normalized = DocumentTags.Normalize(tags);
        return RunCaptureHistoryActionAsync(async () =>
        {
            CaptureDocument? document = await GetHistoryCaptureAsync(new DocumentId(value), cancellationToken).ConfigureAwait(true);
            if (document is null || DocumentTags.Read(document).SequenceEqual(normalized)) return;
            DocumentMetadata previousMetadata = document.Metadata;
            bool isCurrent = _currentDocumentId == document.Id;
            bool persisted = false;
            DocumentTags.Set(document, normalized);
            try
            {
                if (isCurrent)
                    await SaveCurrentDocumentAsync("Updated capture tags.", cancellationToken).ConfigureAwait(true);
                else
                {
                    await _documentRepository!.SaveAsync(document, cancellationToken).ConfigureAwait(true);
                    persisted = true;
                    await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
                    StatusText = "Updated capture tags.";
                }
            }
            catch
            {
                // A later catalog/recovery failure must not undo a successful save in memory.
                if (!persisted && (!isCurrent || !_pendingDocumentSave.IsCompletedSuccessfully))
                {
                    document.Metadata = previousMetadata;
                    if (isCurrent) _pendingDocumentSave = Task.CompletedTask;
                    OnPropertyChanged(nameof(CurrentCaptureTagsLabel));
                }
                throw;
            }
        }, cancellationToken);
    }
}
