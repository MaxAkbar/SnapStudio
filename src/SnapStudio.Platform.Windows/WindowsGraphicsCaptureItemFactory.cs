using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemInterfaceId =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public GraphicsCaptureItem CreateForMonitor(nint monitorHandle)
    {
        if (monitorHandle == 0)
        {
            throw new ArgumentException("A valid monitor handle is required.", nameof(monitorHandle));
        }

        IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemInterfaceId;
        int result = interop.CreateForMonitor(monitorHandle, ref iid, out nint item);
        Marshal.ThrowExceptionForHR(result);

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            Marshal.Release(item);
        }
    }

    public GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemInterfaceId;
        int result = interop.CreateForWindow(windowHandle, ref iid, out nint item);
        Marshal.ThrowExceptionForHR(result);

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            Marshal.Release(item);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(
            nint window,
            ref Guid iid,
            out nint result);

        [PreserveSig]
        int CreateForMonitor(
            nint monitor,
            ref Guid iid,
            out nint result);
    }
}
