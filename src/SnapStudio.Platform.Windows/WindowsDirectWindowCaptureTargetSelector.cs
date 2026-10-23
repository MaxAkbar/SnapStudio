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
    private readonly IWindowsCaptureTargetPicker? _picker;

    public WindowsDirectWindowCaptureTargetSelector(
        nint ownerWindowHandle,
        WindowsGraphicsCaptureItemRegistry registry,
        WindowsTopLevelWindowService? windows = null,
        WindowsGraphicsCaptureItemFactory? itemFactory = null,
        IWindowsCaptureTargetPicker? picker = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _ownerWindowHandle = ownerWindowHandle;
        _registry = registry;
        _windows = windows ?? new WindowsTopLevelWindowService();
        _itemFactory = itemFactory ?? new WindowsGraphicsCaptureItemFactory();
        _picker = picker;
    }

    public async Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0
            || request.SelectionMode != CaptureTargetSelectionMode.Automatic
            || !request.AllowedTargets.Contains(CaptureTargetKind.Window))
        {
            return null;
        }

        WindowsTopLevelWindow? selected = _picker is null ? null
            : await _picker.PickWindowAsync(() => _windows.GetCaptureCandidates(_ownerWindowHandle), cancellationToken)
                .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected is null) return null;
        WindowsTopLevelWindow window = _windows.GetCaptureCandidates(_ownerWindowHandle)
            .FirstOrDefault(candidate => candidate.IsSameWindow(selected))
            ?? throw new InvalidOperationException("That window closed or became unavailable. Choose another window.");

        GraphicsCaptureItem item = _itemFactory.CreateForWindow(window.Handle);
        string targetId = _registry.Register(item, new WindowsCaptureTargetGeometry(window.Bounds, window.Handle));

        return new CaptureTargetSelection(
            CaptureTargetKind.Window,
            targetId,
            new RectD(0, 0, window.Bounds.Width, window.Bounds.Height),
            new Dictionary<string, string>
            {
                ["selectionMode"] = CaptureTargetSelectionMode.Picker.ToString(),
                ["windowTitle"] = window.Title,
                ["windowProcessName"] = window.ProcessName,
                ["windowBoundsX"] = window.Bounds.X.ToString(CultureInfo.InvariantCulture),
                ["windowBoundsY"] = window.Bounds.Y.ToString(CultureInfo.InvariantCulture),
                ["windowBoundsWidth"] = window.Bounds.Width.ToString(CultureInfo.InvariantCulture),
                ["windowBoundsHeight"] = window.Bounds.Height.ToString(CultureInfo.InvariantCulture)
            });
    }
}
