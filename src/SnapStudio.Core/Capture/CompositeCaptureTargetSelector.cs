namespace SnapStudio.Core.Capture;

public sealed class CompositeCaptureTargetSelector : ICaptureTargetSelector
{
    private readonly IReadOnlyList<ICaptureTargetSelector> _selectors;

    public CompositeCaptureTargetSelector(IEnumerable<ICaptureTargetSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);

        _selectors = selectors.ToArray();
    }

    public async Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (ICaptureTargetSelector selector in _selectors)
        {
            CaptureTargetSelection? selection = await selector
                .SelectTargetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (selection is not null)
            {
                return selection;
            }
        }

        return null;
    }
}
