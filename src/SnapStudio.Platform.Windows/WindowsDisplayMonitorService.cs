using System.Runtime.InteropServices;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsDisplayMonitor(
    nint Handle,
    RectD Bounds,
    bool IsPrimary,
    string DeviceName)
{
    public int Number { get; init; }

    public int Dpi { get; init; } = 96;

    public string Title => $"Display {Number}" + (IsPrimary ? " · Primary" : string.Empty);

    public string Resolution => $"{Bounds.Width:0} × {Bounds.Height:0} · {Math.Round(Dpi * 100d / 96):0}%";
}

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
                        info.DeviceName)
                    {
                        Dpi = GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 ? (int)dpiX : 96
                    });
                }

                return true;
            },
            0);

        return succeeded
            ? NumberMonitors(monitors)
            : [];
    }

    // Adapted from ExactFrame's DisplayService: physical bounds and left-to-right numbering.
    public static IReadOnlyList<WindowsDisplayMonitor> NumberMonitors(IEnumerable<WindowsDisplayMonitor> monitors) => monitors
        .OrderBy(monitor => monitor.Bounds.X)
        .ThenBy(monitor => monitor.Bounds.Y)
        .Select((monitor, index) => monitor with { Number = index + 1 })
        .ToArray();

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

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
