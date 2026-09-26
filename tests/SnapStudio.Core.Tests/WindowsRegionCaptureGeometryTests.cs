using SnapStudio.Core.Primitives;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsRegionCaptureGeometryTests
{
    [TestMethod]
    public void CreateVirtualScreenBounds_ReturnsUnionOfMonitorBounds()
    {
        WindowsDisplayMonitor[] monitors =
        [
            new(new nint(1), new RectD(-1280, 0, 1280, 720), IsPrimary: false, "left"),
            new(new nint(2), new RectD(0, 0, 1920, 1080), IsPrimary: true, "primary")
        ];

        RectD bounds = WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(monitors);

        Assert.AreEqual(new RectD(-1280, 0, 3200, 1080), bounds);
    }

    [TestMethod]
    public void SelectMonitorForRegion_ReturnsMonitorWithLargestIntersection()
    {
        WindowsDisplayMonitor[] monitors =
        [
            new(new nint(1), new RectD(0, 0, 100, 100), IsPrimary: true, "primary"),
            new(new nint(2), new RectD(100, 0, 100, 100), IsPrimary: false, "right")
        ];

        WindowsDisplayMonitor? monitor = WindowsRegionCaptureGeometry.SelectMonitorForRegion(
            monitors,
            new RectD(80, 10, 90, 80));

        Assert.IsNotNull(monitor);
        Assert.AreEqual(new nint(2), monitor.Handle);
    }

    [TestMethod]
    public void ToMonitorLocalBounds_ClampsRegionToMonitorAndConvertsOrigin()
    {
        var monitor = new WindowsDisplayMonitor(
            new nint(1),
            new RectD(100, 50, 200, 150),
            IsPrimary: true,
            "primary");

        RectD localBounds = WindowsRegionCaptureGeometry.ToMonitorLocalBounds(
            new RectD(80, 70, 90, 40),
            monitor);

        Assert.AreEqual(new RectD(0, 20, 70, 40), localBounds);
    }

    [TestMethod]
    public void SelectPrimaryMonitor_ReturnsPrimaryMonitorWhenAvailable()
    {
        WindowsDisplayMonitor[] monitors =
        [
            new(new nint(1), new RectD(0, 0, 100, 100), IsPrimary: false, "secondary"),
            new(new nint(2), new RectD(100, 0, 100, 100), IsPrimary: true, "primary")
        ];

        WindowsDisplayMonitor? monitor = WindowsDirectCaptureGeometry.SelectPrimaryMonitor(monitors);

        Assert.IsNotNull(monitor);
        Assert.AreEqual(new nint(2), monitor.Handle);
    }

    [TestMethod]
    public void SelectPrimaryMonitor_WhenNoPrimaryExists_ReturnsLargestMonitor()
    {
        WindowsDisplayMonitor[] monitors =
        [
            new(new nint(1), new RectD(0, 0, 100, 100), IsPrimary: false, "small"),
            new(new nint(2), new RectD(100, 0, 200, 200), IsPrimary: false, "large")
        ];

        WindowsDisplayMonitor? monitor = WindowsDirectCaptureGeometry.SelectPrimaryMonitor(monitors);

        Assert.IsNotNull(monitor);
        Assert.AreEqual(new nint(2), monitor.Handle);
    }
}
