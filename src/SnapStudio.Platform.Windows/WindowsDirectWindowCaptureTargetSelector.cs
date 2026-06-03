using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using System.Globalization;
using Windows.Graphics.Capture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsDirectWindowCaptureTargetSelector : ICaptureTargetSelector
{
    private readonly nint _ownerWindowHandle;
    private readonly WindowsGraphicsCaptureItemFactory _itemFactory;
    private readonly WindowsGraphicsCaptureItemRegistry _registry;
    private readonly WindowsTopLevelWindowService _windows;

    public WindowsDirectWindowCaptureTargetSelector(
        nint ownerWindowHandle,
        WindowsGraphicsCaptureItemRegistry registry,
        WindowsTopLevelWindowService? windows = null,
        WindowsGraphicsCaptureItemFactory? itemFactory = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _ownerWindowHandle = ownerWindowHandle;
        _registry = registry;
        _windows = windows ?? new WindowsTopLevelWindowService();
        _itemFactory = itemFactory ?? new WindowsGraphicsCaptureItemFactory();
    }

    public Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0
            || request.SelectionMode != CaptureTargetSelectionMode.Automatic
            || !request.AllowedTargets.Contains(CaptureTargetKind.Window))
        {
            return Task.FromResult<CaptureTargetSelection?>(null);
        }

        WindowsTopLevelWindow? window = _windows
            .GetCaptureCandidates(_ownerWindowHandle)
            .FirstOrDefault();

        if (window is null)
        {
            return Task.FromResult<CaptureTargetSelection?>(null);
        }

        GraphicsCaptureItem item = _itemFactory.CreateForWindow(window.Handle);
        string targetId = _registry.Register(item);

        return Task.FromResult<CaptureTargetSelection?>(new CaptureTargetSelection(
            CaptureTargetKind.Window,
            targetId,
            new RectD(0, 0, window.Bounds.Width, window.Bounds.Height),
            new Dictionary<string, string>
            {
                ["selectionMode"] = request.SelectionMode.ToString(),
                ["windowTitle"] = window.Title,
                ["windowProcessName"] = window.ProcessName,
                ["windowBoundsX"] = window.Bounds.X.ToString(CultureInfo.InvariantCulture),
                ["windowBoundsY"] = window.Bounds.Y.ToString(CultureInfo.InvariantCulture),
                ["windowBoundsWidth"] = window.Bounds.Width.ToString(CultureInfo.InvariantCulture),
                ["windowBoundsHeight"] = window.Bounds.Height.ToString(CultureInfo.InvariantCulture)
            }));
    }
}
