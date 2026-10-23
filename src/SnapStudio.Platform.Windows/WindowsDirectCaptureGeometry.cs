namespace SnapStudio.Platform.Windows;

public static class WindowsDirectCaptureGeometry
{
    // ExactFrame rechecks the display snapshot after a delayed screen operation.
    public static bool HasSameDisplayLayout(
        IEnumerable<WindowsDisplayMonitor> expected,
        IEnumerable<WindowsDisplayMonitor> current) => expected.SequenceEqual(current);

    public static WindowsDisplayMonitor? SelectPrimaryMonitor(
        IReadOnlyCollection<WindowsDisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        return monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? monitors.OrderByDescending(monitor => monitor.Bounds.Width * monitor.Bounds.Height).FirstOrDefault();
    }
}
