namespace SnapStudio.Platform.Windows;

public static class WindowsDirectCaptureGeometry
{
    public static WindowsDisplayMonitor? SelectPrimaryMonitor(
        IReadOnlyCollection<WindowsDisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        return monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? monitors.OrderByDescending(monitor => monitor.Bounds.Width * monitor.Bounds.Height).FirstOrDefault();
    }
}
