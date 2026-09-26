using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class DocumentThumbnailRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_SupersedesBatchesWithoutCancellingActiveRender()
    {
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        using var lifetime = new CancellationTokenSource();
        var started = NewCompletion();
        var finishRender = NewCompletion();
        DocumentSummary first = CreateSummary("first");
        DocumentSummary skippedRemainder = CreateSummary("skipped remainder");
        DocumentSummary skippedBatch = CreateSummary("skipped batch");
        DocumentSummary latest = CreateSummary("latest");
        var rendered = new List<DocumentId>();
        var published = new List<DocumentId>();
        int activeRenders = 0;
        int maximumActiveRenders = 0;
        CancellationToken renderToken = default;

        async Task<string?> ResolveAsync(DocumentSummary summary, CancellationToken token)
        {
            maximumActiveRenders = Math.Max(maximumActiveRenders, ++activeRenders);
            rendered.Add(summary.Id);
            try
            {
                if (summary.Id == first.Id)
                {
                    renderToken = token;
                    started.SetResult();
                    await finishRender.Task.WaitAsync(token);
                }

                return $"{summary.Id}.png";
            }
            finally
            {
                activeRenders--;
            }
        }

        void Publish(DocumentSummary summary, string path) => published.Add(summary.Id);

        Task firstRefresh = coordinator.RefreshAsync(
            [first, skippedRemainder], ResolveAsync, Publish, lifetime.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task skippedRefresh = coordinator.RefreshAsync(
            [skippedBatch], ResolveAsync, Publish, lifetime.Token);
        Task latestRefresh = coordinator.RefreshAsync(
            [latest], ResolveAsync, Publish, lifetime.Token);

        Assert.Equal(lifetime.Token, renderToken);
        Assert.True(renderToken.CanBeCanceled);
        Assert.False(renderToken.IsCancellationRequested);
        Assert.Equal([first.Id], rendered);

        finishRender.SetResult();
        await Task.WhenAll(firstRefresh, skippedRefresh, latestRefresh)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([first.Id, latest.Id], rendered);
        Assert.Equal([latest.Id], published);
        Assert.Equal(1, maximumActiveRenders);
        Assert.True(firstRefresh.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RefreshAsync_EmptyLatestBatchSuppressesStalePublication()
    {
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        var started = NewCompletion();
        var finishRender = NewCompletion();
        var published = new List<string>();

        async Task<string?> ResolveAsync(DocumentSummary summary, CancellationToken token)
        {
            started.SetResult();
            await finishRender.Task;
            return "old.png";
        }

        Task first = coordinator.RefreshAsync(
            [CreateSummary("old")], ResolveAsync, (_, path) => published.Add(path), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task empty = coordinator.RefreshAsync(
            [], ResolveAsync, (_, path) => published.Add(path), CancellationToken.None);
        finishRender.SetResult();
        await Task.WhenAll(first, empty).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(published);
    }

    [Fact]
    public async Task RefreshAsync_LifetimeCancellationReachesRenderAndCancelsQueuedWork()
    {
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        using var lifetime = new CancellationTokenSource();
        var started = NewCompletion();
        int renderCount = 0;
        int publishCount = 0;

        async Task<string?> ResolveAsync(DocumentSummary summary, CancellationToken token)
        {
            renderCount++;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "unexpected.png";
        }

        Task first = coordinator.RefreshAsync(
            [CreateSummary("active")], ResolveAsync, (_, _) => publishCount++, lifetime.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task queued = coordinator.RefreshAsync(
            [CreateSummary("queued")], ResolveAsync, (_, _) => publishCount++, lifetime.Token);
        lifetime.Cancel();

        OperationCanceledException firstCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => first.WaitAsync(TimeSpan.FromSeconds(5)));
        OperationCanceledException queuedCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(lifetime.Token, firstCancellation.CancellationToken);
        Assert.Equal(lifetime.Token, queuedCancellation.CancellationToken);
        Assert.Equal(1, renderCount);
        Assert.Equal(0, publishCount);
    }

    [Fact]
    public async Task RefreshAsync_CancellationAfterResolverReturnsDoesNotPublish()
    {
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        using var lifetime = new CancellationTokenSource();
        int publishCount = 0;

        Task<string?> ResolveAsync(DocumentSummary summary, CancellationToken token)
        {
            lifetime.Cancel();
            return Task.FromResult<string?>("stale.png");
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RefreshAsync(
            [CreateSummary("active")], ResolveAsync, (_, _) => publishCount++, lifetime.Token));

        Assert.Equal(0, publishCount);
    }

    [Fact]
    public async Task RefreshAsync_SupersededRenderFailureStillPropagatesAndReleasesGate()
    {
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        var started = NewCompletion();
        var finishRender = NewCompletion();
        DocumentSummary failed = CreateSummary("failed");
        var failure = new IOException("The source image could not be read.");
        var published = new List<string>();

        async Task<string?> ResolveAsync(DocumentSummary summary, CancellationToken token)
        {
            if (summary.Id == failed.Id)
            {
                started.SetResult();
                await finishRender.Task;
                throw failure;
            }

            return "latest.png";
        }

        Task first = coordinator.RefreshAsync(
            [failed], ResolveAsync, (_, path) => published.Add(path), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task latest = coordinator.RefreshAsync(
            [CreateSummary("latest")], ResolveAsync, (_, path) => published.Add(path), CancellationToken.None);
        finishRender.SetResult();

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(
            () => first.WaitAsync(TimeSpan.FromSeconds(5))));
        await latest.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["latest.png"], published);
    }

    [Fact]
    public async Task RefreshAsync_UnrelatedCancellationIsNotSwallowed()
    {
        var coordinator = new DocumentThumbnailRefreshCoordinator();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RefreshAsync(
                [CreateSummary("active")],
                (_, _) => Task.FromCanceled<string?>(unrelated.Token),
                (_, _) => Assert.Fail("A cancelled render must not publish."),
                CancellationToken.None));

        Assert.Equal(unrelated.Token, exception.CancellationToken);
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static DocumentSummary CreateSummary(string title) => new(
        DocumentId.New(), title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "source.png", 1);
}
