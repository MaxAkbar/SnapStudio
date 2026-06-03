using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCapturePickerTargetSelector : ICaptureTargetSelector
{
    private readonly nint _ownerWindowHandle;
    private readonly WindowsGraphicsCaptureItemRegistry _registry;

    public WindowsGraphicsCapturePickerTargetSelector(
        nint ownerWindowHandle,
        WindowsGraphicsCaptureItemRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _ownerWindowHandle = ownerWindowHandle;
        _registry = registry;
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
