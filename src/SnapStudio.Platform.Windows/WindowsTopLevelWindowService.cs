using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsTopLevelWindow(
    nint Handle,
    string Title,
    RectD Bounds,
    string ProcessName)
{
    public uint ProcessId { get; init; }

    public uint ThreadId { get; init; }

    // ExactFrame identifies a window by handle, process and thread, since handles can be reused.
    public bool IsSameWindow(WindowsTopLevelWindow other) => Handle == other.Handle
        && ProcessId == other.ProcessId && ThreadId == other.ThreadId;

    public string Description => $"{ProcessName}.exe · {Bounds.Width:0} × {Bounds.Height:0}";
}

public sealed class WindowsTopLevelWindowService
{
    private const uint GwOwner = 4;
    private const int DwmwaCloaked = 14;
    private const int DwmwaExtendedFrameBounds = 9;

    public static RectD? GetVisibleBounds(nint window)
    {
        if (!IsWindowVisible(window) || IsIconic(window)) return null;
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out NativeRect visible, Marshal.SizeOf<NativeRect>()) == 0)
            return visible.ToRectD();
        return GetWindowRect(window, out NativeRect bounds) ? bounds.ToRectD() : null;
    }

    public IReadOnlyList<WindowsTopLevelWindow> GetCaptureCandidates(nint excludedWindowHandle)
    {
        int currentProcessId = Environment.ProcessId;
        var candidates = new List<WindowsTopLevelWindow>();

        EnumWindows(
            (windowHandle, _) =>
            {
                if (!TryCreateCandidate(windowHandle, excludedWindowHandle, currentProcessId, out WindowsTopLevelWindow? candidate))
                {
                    return true;
                }

                candidates.Add(candidate!);
                return true;
            },
            0);

        return candidates.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static bool TryCreateCandidate(
        nint windowHandle,
        nint excludedWindowHandle,
        int currentProcessId,
        out WindowsTopLevelWindow? candidate)
    {
        candidate = null;

        if (windowHandle == 0
            || windowHandle == excludedWindowHandle
            || !IsWindowVisible(windowHandle)
            || IsIconic(windowHandle)
            || GetWindow(windowHandle, GwOwner) != 0)
        {
            return false;
        }

        uint threadId = GetWindowThreadProcessId(windowHandle, out uint processId);
        if (processId == 0 || processId == currentProcessId
            || (DwmGetWindowAttributeInt(windowHandle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0))
        {
            return false;
        }

        string title = GetWindowTitle(windowHandle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!GetWindowRect(windowHandle, out NativeRect rect))
        {
            return false;
        }

        // Reuse ExactFrame's visible-frame measurement to exclude invisible resize borders.
        RectD bounds = DwmGetWindowAttribute(windowHandle, DwmwaExtendedFrameBounds, out NativeRect visible, Marshal.SizeOf<NativeRect>()) == 0
            ? visible.ToRectD() : rect.ToRectD();
        if (bounds.Width < 32 || bounds.Height < 32)
        {
            return false;
        }

        candidate = new WindowsTopLevelWindow(
            windowHandle,
            title,
            bounds,
            ResolveProcessName((int)processId))
        {
            ProcessId = processId,
            ThreadId = threadId
        };
        return true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(nint window, int attribute, out int value, int size);

    private static string GetWindowTitle(nint windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string ResolveProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder title, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect bounds);

    private delegate bool EnumWindowsProc(nint windowHandle, nint lParam);

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
