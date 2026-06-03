using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public static class WindowsRegionCaptureGeometry
{
    public static RectD CreateVirtualScreenBounds(IReadOnlyCollection<WindowsDisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            return new RectD(0, 0, 0, 0);
        }

        double left = monitors.Min(monitor => monitor.Bounds.X);
        double top = monitors.Min(monitor => monitor.Bounds.Y);
        double right = monitors.Max(monitor => monitor.Bounds.Right);
        double bottom = monitors.Max(monitor => monitor.Bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    public static WindowsDisplayMonitor? SelectMonitorForRegion(
        IReadOnlyCollection<WindowsDisplayMonitor> monitors,
        RectD region)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        return monitors
            .Select(monitor => new
            {
                Monitor = monitor,
                Area = CalculateIntersectionArea(monitor.Bounds, region)
            })
            .Where(candidate => candidate.Area > 0)
            .OrderByDescending(candidate => candidate.Area)
            .ThenByDescending(candidate => candidate.Monitor.IsPrimary)
            .Select(candidate => candidate.Monitor)
            .FirstOrDefault();
    }

    public static RectD ToMonitorLocalBounds(RectD region, WindowsDisplayMonitor monitor)
    {
        double left = Math.Max(region.X, monitor.Bounds.X);
        double top = Math.Max(region.Y, monitor.Bounds.Y);
        double right = Math.Min(region.Right, monitor.Bounds.Right);
        double bottom = Math.Min(region.Bottom, monitor.Bounds.Bottom);

        return new RectD(
            Math.Max(0, left - monitor.Bounds.X),
            Math.Max(0, top - monitor.Bounds.Y),
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }

    public static RectD Normalize(RectD region)
    {
        double x = Math.Min(region.X, region.Right);
        double y = Math.Min(region.Y, region.Bottom);
        double right = Math.Max(region.X, region.Right);
        double bottom = Math.Max(region.Y, region.Bottom);

        return new RectD(x, y, right - x, bottom - y);
    }

    private static double CalculateIntersectionArea(RectD first, RectD second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.Right, second.Right);
        double bottom = Math.Min(first.Bottom, second.Bottom);

        return Math.Max(0, right - left) * Math.Max(0, bottom - top);
    }
}
