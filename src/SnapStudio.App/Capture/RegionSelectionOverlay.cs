using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.Capture;

public sealed class RegionSelectionOverlay : IRegionSelectionService
{
    private readonly IScreenPreviewService? _screenPreview;

    public RegionSelectionOverlay(IScreenPreviewService? screenPreview = null)
    {
        _screenPreview = screenPreview;
    }

    public Task<RectD?> SelectRegionAsync(
        RectD virtualScreenBounds,
        CancellationToken cancellationToken)
    {
        return SelectRegionCoreAsync(virtualScreenBounds, cancellationToken);
    }

    private async Task<RectD?> SelectRegionCoreAsync(
        RectD virtualScreenBounds,
        CancellationToken cancellationToken)
    {
        if (virtualScreenBounds.Width <= 0 || virtualScreenBounds.Height <= 0)
        {
            return null;
        }

        ScreenPreviewImage? preview = _screenPreview is null
            ? null
            : await _screenPreview.CapturePreviewAsync(virtualScreenBounds, cancellationToken).ConfigureAwait(true);

        try
        {
            var window = new RegionSelectionWindow(virtualScreenBounds, preview);
            return await window.SelectAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            TryDeletePreview(preview);
        }
    }

    private static void TryDeletePreview(ScreenPreviewImage? preview)
    {
        if (preview is null)
        {
            return;
        }

        try
        {
            File.Delete(preview.Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
