using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using System.Globalization;
using Windows.Graphics.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsRegionCaptureTargetSelector : ICaptureTargetSelector
{
    private const double MinimumRegionSize = 4;
    private readonly WindowsDisplayMonitorService _displayMonitors;
    private readonly WindowsGraphicsCaptureItemFactory _itemFactory;
    private readonly WindowsGraphicsCaptureItemRegistry _registry;
    private readonly IRegionSelectionService _regionSelection;

    public WindowsRegionCaptureTargetSelector(
        IRegionSelectionService regionSelection,
        WindowsGraphicsCaptureItemRegistry registry,
        WindowsDisplayMonitorService? displayMonitors = null,
        WindowsGraphicsCaptureItemFactory? itemFactory = null)
    {
        ArgumentNullException.ThrowIfNull(regionSelection);
        ArgumentNullException.ThrowIfNull(registry);

        _regionSelection = regionSelection;
        _registry = registry;
        _displayMonitors = displayMonitors ?? new WindowsDisplayMonitorService();
        _itemFactory = itemFactory ?? new WindowsGraphicsCaptureItemFactory();
    }

    public async Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SelectionMode != CaptureTargetSelectionMode.Automatic
            || !request.AllowedTargets.Contains(CaptureTargetKind.Region))
        {
            return null;
        }

        IReadOnlyList<WindowsDisplayMonitor> monitors = _displayMonitors.GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        RectD? selectedRegion = await _regionSelection
            .SelectRegionAsync(
                WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(monitors),
                cancellationToken)
            .ConfigureAwait(true);

        if (selectedRegion is null)
        {
            return null;
        }

        RectD normalizedRegion = WindowsRegionCaptureGeometry.Normalize(selectedRegion.Value);
        if (normalizedRegion.Width < MinimumRegionSize || normalizedRegion.Height < MinimumRegionSize)
        {
            return null;
        }

        WindowsDisplayMonitor? monitor = WindowsRegionCaptureGeometry.SelectMonitorForRegion(
            monitors,
            normalizedRegion);

        if (monitor is null)
        {
            return null;
        }

        RectD monitorLocalBounds = WindowsRegionCaptureGeometry.ToMonitorLocalBounds(
            normalizedRegion,
            monitor);

        if (monitorLocalBounds.Width < MinimumRegionSize || monitorLocalBounds.Height < MinimumRegionSize)
        {
            return null;
        }

        GraphicsCaptureItem item = _itemFactory.CreateForMonitor(monitor.Handle);
        string targetId = _registry.Register(item);

        return new CaptureTargetSelection(
            CaptureTargetKind.Region,
            targetId,
            monitorLocalBounds,
            new Dictionary<string, string>
            {
                ["selectionMode"] = request.SelectionMode.ToString(),
                ["monitorDeviceName"] = monitor.DeviceName,
                ["monitorIsPrimary"] = monitor.IsPrimary.ToString(),
                ["screenBoundsX"] = normalizedRegion.X.ToString(CultureInfo.InvariantCulture),
                ["screenBoundsY"] = normalizedRegion.Y.ToString(CultureInfo.InvariantCulture),
                ["screenBoundsWidth"] = normalizedRegion.Width.ToString(CultureInfo.InvariantCulture),
                ["screenBoundsHeight"] = normalizedRegion.Height.ToString(CultureInfo.InvariantCulture),
                ["monitorBoundsX"] = monitor.Bounds.X.ToString(CultureInfo.InvariantCulture),
                ["monitorBoundsY"] = monitor.Bounds.Y.ToString(CultureInfo.InvariantCulture),
                ["monitorBoundsWidth"] = monitor.Bounds.Width.ToString(CultureInfo.InvariantCulture),
                ["monitorBoundsHeight"] = monitor.Bounds.Height.ToString(CultureInfo.InvariantCulture)
            });
    }
}
