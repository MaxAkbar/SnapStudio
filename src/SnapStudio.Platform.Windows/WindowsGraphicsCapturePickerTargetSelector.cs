using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCapturePickerTargetSelector : ICaptureTargetSelector
{
    private readonly nint _ownerWindowHandle;
    private readonly WindowsGraphicsCaptureItemRegistry _registry;
    private readonly IWindowsCaptureTargetPicker? _targetPicker;

    public WindowsGraphicsCapturePickerTargetSelector(
        nint ownerWindowHandle,
        WindowsGraphicsCaptureItemRegistry registry,
        IWindowsCaptureTargetPicker? targetPicker = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _ownerWindowHandle = ownerWindowHandle;
        _registry = registry;
        _targetPicker = targetPicker;
    }

    public async Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0
            || request.SelectionMode != CaptureTargetSelectionMode.Picker
            || !SupportsPickerTarget(request.AllowedTargets))
        {
            return null;
        }

        // The system picker exposes no desktop origin or window handle. Use explicit
        // targets for editable pointers; recording retains the native picker.
        if (request.IncludeCursor)
            return await SelectEditableTargetAsync(request, cancellationToken).ConfigureAwait(true);

        var picker = new GraphicsCapturePicker();
        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        GraphicsCaptureItem? item = await picker.PickSingleItemAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (item is null)
        {
            return null;
        }

        string targetId = _registry.Register(item);
        CaptureTargetKind targetKind = ResolveTargetKind(request.AllowedTargets);

        return new CaptureTargetSelection(
            targetKind,
            targetId,
            new RectD(0, 0, item.Size.Width, item.Size.Height),
            new Dictionary<string, string>
            {
                ["selectionMode"] = request.SelectionMode.ToString(),
                ["displayName"] = item.DisplayName
            });
    }

    private async Task<CaptureTargetSelection?> SelectEditableTargetAsync(CaptureTargetRequest request, CancellationToken cancellationToken)
    {
        if (_targetPicker is null) return null;
        var monitors = new WindowsDisplayMonitorService();
        var windows = new WindowsTopLevelWindowService();
        IReadOnlyList<WindowsCaptureTargetChoice> GetTargets() =>
            [.. (request.AllowedTargets.Contains(CaptureTargetKind.Display) || request.AllowedTargets.Contains(CaptureTargetKind.FullScreen)
                ? monitors.GetMonitors().Select(monitor => new WindowsCaptureTargetChoice(monitor, null)) : []),
             .. (request.AllowedTargets.Contains(CaptureTargetKind.Window)
                ? windows.GetCaptureCandidates(_ownerWindowHandle).Select(window => new WindowsCaptureTargetChoice(null, window)) : [])];
        WindowsCaptureTargetChoice? selected = await _targetPicker.PickTargetAsync(GetTargets, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected is null) return null;
        var factory = new WindowsGraphicsCaptureItemFactory();
        GraphicsCaptureItem item;
        WindowsCaptureTargetGeometry geometry;
        CaptureTargetKind kind;
        if (selected.Monitor is WindowsDisplayMonitor chosenMonitor)
        {
            WindowsDisplayMonitor monitor = monitors.GetMonitors().FirstOrDefault(candidate => candidate.Handle == chosenMonitor.Handle
                && candidate.DeviceName == chosenMonitor.DeviceName && candidate.Bounds == chosenMonitor.Bounds)
                ?? throw new InvalidOperationException("The display configuration changed. Choose the display again.");
            item = factory.CreateForMonitor(monitor.Handle);
            geometry = new WindowsCaptureTargetGeometry(monitor.Bounds);
            kind = request.AllowedTargets.Contains(CaptureTargetKind.Display) ? CaptureTargetKind.Display : CaptureTargetKind.FullScreen;
        }
        else
        {
            WindowsTopLevelWindow window = windows.GetCaptureCandidates(_ownerWindowHandle).FirstOrDefault(candidate => candidate.IsSameWindow(selected.Window!))
                ?? throw new InvalidOperationException("That window closed or became unavailable. Choose another window.");
            item = factory.CreateForWindow(window.Handle);
            geometry = new WindowsCaptureTargetGeometry(window.Bounds, window.Handle);
            kind = CaptureTargetKind.Window;
        }
        return new CaptureTargetSelection(kind, _registry.Register(item, geometry), new RectD(0, 0, item.Size.Width, item.Size.Height),
            new Dictionary<string, string> { ["selectionMode"] = "Picker", ["displayName"] = item.DisplayName });
    }

    private static bool SupportsPickerTarget(IReadOnlyCollection<CaptureTargetKind> allowedTargets)
    {
        return allowedTargets.Contains(CaptureTargetKind.FullScreen)
            || allowedTargets.Contains(CaptureTargetKind.Display)
            || allowedTargets.Contains(CaptureTargetKind.Window);
    }

    private static CaptureTargetKind ResolveTargetKind(IReadOnlyCollection<CaptureTargetKind> allowedTargets)
    {
        if (allowedTargets.Contains(CaptureTargetKind.FullScreen))
        {
            return CaptureTargetKind.FullScreen;
        }

        if (allowedTargets.Contains(CaptureTargetKind.Display))
        {
            return CaptureTargetKind.Display;
        }

        return CaptureTargetKind.Window;
    }
}
