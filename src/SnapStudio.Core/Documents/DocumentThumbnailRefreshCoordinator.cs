namespace SnapStudio.Core.Documents;

/// <summary>
/// Serializes thumbnail refreshes and publishes only the latest batch.
/// Superseding a batch lets its current cache write finish; cancellation is
/// reserved for the owner's lifetime and still reaches the renderer.
/// </summary>
public sealed class DocumentThumbnailRefreshCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _version;

    public async Task RefreshAsync(
        IReadOnlyList<DocumentSummary> summaries,
        Func<DocumentSummary, CancellationToken, Task<string?>> resolveThumbnailAsync,
        Action<DocumentSummary, string> publishThumbnail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(resolveThumbnailAsync);
        ArgumentNullException.ThrowIfNull(publishThumbnail);

        int version = Interlocked.Increment(ref _version);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            foreach (DocumentSummary summary in summaries)
            {
                if (version != Volatile.Read(ref _version))
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                string? thumbnailPath = await resolveThumbnailAsync(summary, cancellationToken)
                    .ConfigureAwait(true);

                if (version != Volatile.Read(ref _version))
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    // Preserve the caller's context for the UI update.
                    publishThumbnail(summary, thumbnailPath);
                }

                await Task.Yield();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
