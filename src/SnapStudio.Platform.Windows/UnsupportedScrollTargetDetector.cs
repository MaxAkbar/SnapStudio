using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedScrollTargetDetector : IScrollTargetDetector
{
    public Task<IReadOnlyList<ScrollTargetCandidate>> DetectAsync(
        ScrollTargetDetectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<ScrollTargetCandidate>>([]);
    }
}
