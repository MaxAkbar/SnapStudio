using System.Runtime.InteropServices;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsDisplayMonitor(
    nint Handle,
    RectD Bounds,
    bool IsPrimary,
    string DeviceName);

public sealed class WindowsDisplayMonitorService
{
    private const int MonitorInfoPrimary = 0x00000001;

    public IReadOnlyList<WindowsDisplayMonitor> GetMonitors()
    {
        var monitors = new List<WindowsDisplayMonitor>();

        bool succeeded = EnumDisplayMonitors(
            0,
            0,
            (monitor, _, _, _) =>
            {
                var info = MonitorInfoEx.Create();
                if (GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add(new WindowsDisplayMonitor(
                        monitor,
                        info.Monitor.ToRectD(),
                        (info.Flags & MonitorInfoPrimary) == MonitorInfoPrimary,
                        info.DeviceName));
                }

                return true;
            },
            0);

        return succeeded
            ? monitors
            : [];
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clip,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx monitorInfo);

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint hdc,
        nint bounds,
        nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        private const int DeviceNameLength = 32;

        public int Size;

        public NativeRect Monitor;

        public NativeRect WorkArea;

        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameLength)]
        public string DeviceName;

        public static MonitorInfoEx Create()
        {
            return new MonitorInfoEx
            {
                Size = Marshal.SizeOf<MonitorInfoEx>(),
                DeviceName = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        private readonly int _left;
        private readonly int _top;
        private readonly int _right;
        private readonly int _bottom;

        public RectD ToRectD()
        {
            return new RectD(
                _left,
                _top,
                _right - _left,
                _bottom - _top);
        }
    }
}
