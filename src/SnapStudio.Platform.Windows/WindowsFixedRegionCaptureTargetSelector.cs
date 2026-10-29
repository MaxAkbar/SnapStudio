using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

/// <summary>Uses the normal monitor capture path without asking the user to redraw the frame.</summary>
public sealed class WindowsFixedRegionCaptureTargetSelector(RectD bounds, WindowsGraphicsCaptureItemRegistry registry) : ICaptureTargetSelector
{
    public async Task<CaptureTargetSelection?> SelectTargetAsync(CaptureTargetRequest request, CancellationToken token)
    {
        var monitors = new WindowsDisplayMonitorService();
        if (!FitsDisplay(bounds, monitors.GetMonitors().Select(monitor => monitor.Bounds)))
            throw new InvalidOperationException("The entire ExactFrame region must fit on one connected display. Reposition the frame and try again.");
        var selector = new WindowsRegionCaptureTargetSelector(new FixedSelection(bounds), registry, monitors);
        var selection = await selector.SelectTargetAsync(request, token);
        if (selection?.Bounds is not RectD actual || actual.Width != bounds.Width || actual.Height != bounds.Height)
            throw new InvalidOperationException("Display settings changed while preparing the frame. Check the frame and capture again.");
        return selection;
    }
    public static bool FitsDisplay(RectD region, IEnumerable<RectD> displays) => region.Width >= 4 && region.Height >= 4
        && new[] { region.X, region.Y, region.Right, region.Bottom }.All(double.IsFinite) && displays.Any(display => region.X >= display.X
        && region.Y >= display.Y && region.Right <= display.Right && region.Bottom <= display.Bottom);
    private sealed class FixedSelection(RectD bounds) : IRegionSelectionService
    {
        public Task<RectD?> SelectRegionAsync(RectD virtualScreenBounds, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<RectD?>(bounds); }
    }
}
