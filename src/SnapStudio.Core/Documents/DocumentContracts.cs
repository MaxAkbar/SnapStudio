using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public interface IDocumentRepository
{
    Task<CaptureDocument> CreateFromCaptureAsync(
        CaptureResult capture,
        CancellationToken cancellationToken);

    Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken cancellationToken);

    Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(DocumentId id, CancellationToken cancellationToken);
}

public interface IDocumentCatalog
{
    Task<IReadOnlyList<DocumentSummary>> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface IDocumentThumbnailCache
{
    Task<DocumentThumbnailResult> EnsureThumbnailAsync(
        DocumentThumbnailRequest request,
        CancellationToken cancellationToken);
}

public interface IDocumentWorkspaceBootstrapper
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken);
}

public interface IEditCommand
{
    string DisplayName { get; }

    ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken);

    ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken);
}

public interface IEditCommandStack
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    string? UndoDisplayName { get; }

    string? RedoDisplayName { get; }

    ValueTask ExecuteAsync(
        CaptureDocument document,
        IEditCommand command,
        CancellationToken cancellationToken);

    ValueTask<bool> UndoAsync(CaptureDocument document, CancellationToken cancellationToken);

    ValueTask<bool> RedoAsync(CaptureDocument document, CancellationToken cancellationToken);

    void Clear();
}
