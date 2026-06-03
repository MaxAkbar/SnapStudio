using System.Collections.Concurrent;
using Windows.Graphics.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCaptureItemRegistry
{
    private readonly ConcurrentDictionary<string, GraphicsCaptureItem> _items = new();

    public string Register(GraphicsCaptureItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string id = Guid.NewGuid().ToString("N");
        _items[id] = item;

        return id;
    }

    public bool TryGet(string id, out GraphicsCaptureItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _items.TryGetValue(id, out item!);
    }
}
