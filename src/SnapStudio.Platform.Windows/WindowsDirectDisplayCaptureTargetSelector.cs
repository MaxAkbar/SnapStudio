using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using System.Globalization;
using Windows.Graphics.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsDirectDisplayCaptureTargetSelector : ICaptureTargetSelector
{
    private readonly WindowsDisplayMonitorService _displayMonitors;
    private readonly WindowsGraphicsCaptureItemFactory _itemFactory;
    private readonly WindowsGraphicsCaptureItemRegistry _registry;

    public WindowsDirectDisplayCaptureTargetSelector(
        WindowsGraphicsCaptureItemRegistry registry,
        WindowsDisplayMonitorService? displayMonitors = null,
        WindowsGraphicsCaptureItemFactory? itemFactory = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        _displayMonitors = displayMonitors ?? new WindowsDisplayMonitorService();
        _itemFactory = itemFactory ?? new WindowsGraphicsCaptureItemFactory();
    }

    public Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SelectionMode != CaptureTargetSelectionMode.Automatic
            || (!request.AllowedTargets.Contains(CaptureTargetKind.Display)
                && !request.AllowedTargets.Contains(CaptureTargetKind.FullScreen)))
        {
            return Task.FromResult<CaptureTargetSelection?>(null);
        }

        IReadOnlyList<WindowsDisplayMonitor> monitors = _displayMonitors.GetMonitors();
        WindowsDisplayMonitor? monitor = WindowsDirectCaptureGeometry.SelectPrimaryMonitor(monitors);
        if (monitor is null)
        {
            return Task.FromResult<CaptureTargetSelection?>(null);
        }

        GraphicsCaptureItem item = _itemFactory.CreateForMonitor(monitor.Handle);
        string targetId = _registry.Register(item);
        CaptureTargetKind targetKind = request.AllowedTargets.Contains(CaptureTargetKind.FullScreen)
            ? CaptureTargetKind.FullScreen
            : CaptureTargetKind.Display;

        return Task.FromResult<CaptureTargetSelection?>(new CaptureTargetSelection(
            targetKind,
            targetId,
            new RectD(0, 0, monitor.Bounds.Width, monitor.Bounds.Height),
            CreateMonitorMetadata(request.SelectionMode, monitor, monitors)));
    }

    private static IReadOnlyDictionary<string, string> CreateMonitorMetadata(
        CaptureTargetSelectionMode selectionMode,
        WindowsDisplayMonitor monitor,
        IReadOnlyCollection<WindowsDisplayMonitor> monitors)
    {
        RectD virtualScreenBounds = WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(monitors);

        return new Dictionary<string, string>
        {
            ["selectionMode"] = selectionMode.ToString(),
            ["monitorDeviceName"] = monitor.DeviceName,
            ["monitorIsPrimary"] = monitor.IsPrimary.ToString(),
            ["monitorBoundsX"] = monitor.Bounds.X.ToString(CultureInfo.InvariantCulture),
            ["monitorBoundsY"] = monitor.Bounds.Y.ToString(CultureInfo.InvariantCulture),
            ["monitorBoundsWidth"] = monitor.Bounds.Width.ToString(CultureInfo.InvariantCulture),
            ["monitorBoundsHeight"] = monitor.Bounds.Height.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsX"] = virtualScreenBounds.X.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsY"] = virtualScreenBounds.Y.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsWidth"] = virtualScreenBounds.Width.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsHeight"] = virtualScreenBounds.Height.ToString(CultureInfo.InvariantCulture)
        };
    }
}
