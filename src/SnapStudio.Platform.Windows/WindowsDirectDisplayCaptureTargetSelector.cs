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
    private readonly IWindowsCaptureTargetPicker? _picker;

    public WindowsDirectDisplayCaptureTargetSelector(
        WindowsGraphicsCaptureItemRegistry registry,
        WindowsDisplayMonitorService? displayMonitors = null,
        WindowsGraphicsCaptureItemFactory? itemFactory = null,
        IWindowsCaptureTargetPicker? picker = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        _displayMonitors = displayMonitors ?? new WindowsDisplayMonitorService();
        _itemFactory = itemFactory ?? new WindowsGraphicsCaptureItemFactory();
        _picker = picker;
    }

    public async Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SelectionMode != CaptureTargetSelectionMode.Automatic
            || (!request.AllowedTargets.Contains(CaptureTargetKind.Display)
                && !request.AllowedTargets.Contains(CaptureTargetKind.FullScreen)))
        {
            return null;
        }

        IReadOnlyList<WindowsDisplayMonitor> monitors = _displayMonitors.GetMonitors();
        if (request.AllowedTargets.Contains(CaptureTargetKind.FullScreen))
        {
            if (monitors.Count == 0) return null;
            string desktopId = _registry.RegisterDesktop(monitors.Select(monitor =>
                new WindowsDesktopCaptureTarget(_itemFactory.CreateForMonitor(monitor.Handle), monitor)).ToArray());
            return new CaptureTargetSelection(CaptureTargetKind.FullScreen, desktopId,
                WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(monitors), CreateDesktopMetadata(monitors));
        }

        WindowsDisplayMonitor? selected = _picker is null ? null
            : await _picker.PickDisplayAsync(_displayMonitors.GetMonitors, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected is null) return null;
        monitors = _displayMonitors.GetMonitors();
        WindowsDisplayMonitor? monitor = monitors.FirstOrDefault(candidate => candidate.DeviceName == selected.DeviceName
            && candidate.Handle == selected.Handle && candidate.Bounds == selected.Bounds);
        if (monitor is null)
        {
            throw new InvalidOperationException("The display configuration changed. Choose the display again.");
        }

        GraphicsCaptureItem item = _itemFactory.CreateForMonitor(monitor.Handle);
        string targetId = _registry.Register(item, new WindowsCaptureTargetGeometry(monitor.Bounds));
        return new CaptureTargetSelection(
            CaptureTargetKind.Display,
            targetId,
            new RectD(0, 0, monitor.Bounds.Width, monitor.Bounds.Height),
            CreateMonitorMetadata(CaptureTargetSelectionMode.Picker, monitor, monitors));
    }

    private static IReadOnlyDictionary<string, string> CreateDesktopMetadata(IReadOnlyList<WindowsDisplayMonitor> monitors)
    {
        RectD bounds = WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(monitors);
        var metadata = new Dictionary<string, string>
        {
            ["selectionMode"] = CaptureTargetSelectionMode.Automatic.ToString(),
            ["displayName"] = "All displays",
            ["monitorCount"] = monitors.Count.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsX"] = bounds.X.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsY"] = bounds.Y.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsWidth"] = bounds.Width.ToString(CultureInfo.InvariantCulture),
            ["virtualScreenBoundsHeight"] = bounds.Height.ToString(CultureInfo.InvariantCulture)
        };
        for (int index = 0; index < monitors.Count; index++)
            metadata[$"monitor{index + 1}DeviceName"] = monitors[index].DeviceName;
        return metadata;
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
