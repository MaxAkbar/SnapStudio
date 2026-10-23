using System.Collections.Concurrent;
using Windows.Graphics.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsDesktopCaptureTarget(GraphicsCaptureItem Item, WindowsDisplayMonitor Monitor);
public sealed record WindowsCaptureTargetGeometry(RectD ScreenBounds, nint WindowHandle = 0);

public sealed class WindowsGraphicsCaptureItemRegistry
{
    private readonly ConcurrentDictionary<string, GraphicsCaptureItem> _items = new();
    private readonly ConcurrentDictionary<string, WindowsCaptureTargetGeometry> _geometry = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<WindowsDesktopCaptureTarget>> _desktops = new();

    public string RegisterDesktop(IReadOnlyList<WindowsDesktopCaptureTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0) throw new ArgumentException("At least one display is required.", nameof(targets));
        string id = Guid.NewGuid().ToString("N");
        _desktops[id] = targets.ToArray();
        return id;
    }

    public bool TryGetDesktop(string id, out IReadOnlyList<WindowsDesktopCaptureTarget> targets) => _desktops.TryGetValue(id, out targets!);

    public string Register(GraphicsCaptureItem item, WindowsCaptureTargetGeometry? geometry = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        string id = Guid.NewGuid().ToString("N");
        _items[id] = item;
        if (geometry is not null) _geometry[id] = geometry;

        return id;
    }

    public bool TryGetGeometry(string id, out WindowsCaptureTargetGeometry geometry) => _geometry.TryGetValue(id, out geometry!);

    public bool TryGet(string id, out GraphicsCaptureItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _items.TryGetValue(id, out item!);
    }
}
