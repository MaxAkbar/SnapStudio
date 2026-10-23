using SnapStudio.Core.Rendering;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CanvasViewportStateTests
{
    [TestMethod]
    public void Create_WithInvalidSource_ReturnsEmptyViewport()
    {
        CanvasViewportState viewport = CanvasViewportState.Create(0, 600);

        Assert.IsFalse(viewport.HasSource);
        Assert.AreEqual(0, viewport.DisplayWidth);
        Assert.AreEqual(0, viewport.DisplayHeight);
    }

    [TestMethod]
    public void FitTo_UsesLimitingViewportDimension()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(1600, 900)
            .FitTo(800, 800);

        Assert.AreEqual(0.5, viewport.Zoom, 0.0005);
        Assert.AreEqual(800, viewport.DisplayWidth, 0.0005);
        Assert.AreEqual(450, viewport.DisplayHeight, 0.0005);
    }

    [TestMethod]
    [DataRow(0.01, CanvasViewportState.MinimumZoom)]
    [DataRow(100, CanvasViewportState.MaximumZoom)]
    [DataRow(double.NaN, 1)]
    public void WithZoom_ClampsInvalidZoom(double requestedZoom, double expectedZoom)
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(requestedZoom);

        Assert.AreEqual(expectedZoom, viewport.Zoom);
    }

    [TestMethod]
    [DataRow(120, 1.25)]
    [DataRow(-120, 0.8)]
    [DataRow(240, 1.5625)]
    [DataRow(-240, 0.64)]
    [DataRow(60, 1.118033988749895)]
    [DataRow(int.MaxValue, CanvasViewportState.MaximumZoom)]
    [DataRow(int.MinValue, CanvasViewportState.MinimumZoom)]
    public void ZoomWithWheel_UsesDirectionNotchesAndFractionalDeltasWithinLimits(int delta, double zoom)
    {
        CanvasViewportState viewport = CanvasViewportState.Create(800, 600).ZoomWithWheel(delta);

        Assert.AreEqual(zoom, viewport.Zoom, 0.0000001);
        Assert.AreEqual(800 * zoom, viewport.DisplayWidth, 0.0001);
        Assert.AreEqual(600 * zoom, viewport.DisplayHeight, 0.0001);
    }

    [TestMethod]
    public void ZoomWithWheel_OppositeDeltasRestoreZoomAndLimitsStillAllowTheOppositeDirection()
    {
        CanvasViewportState original = CanvasViewportState.Create(800, 600).WithZoom(1.7);
        Assert.AreEqual(original.Zoom, original.ZoomWithWheel(45).ZoomWithWheel(-45).Zoom, 0.0000001);
        CanvasViewportState maximum = original.WithZoom(CanvasViewportState.MaximumZoom);
        Assert.AreEqual(maximum, maximum.ZoomWithWheel(120));
        Assert.IsTrue(maximum.ZoomWithWheel(-120).Zoom < maximum.Zoom);
        CanvasViewportState minimum = original.WithZoom(CanvasViewportState.MinimumZoom);
        Assert.AreEqual(minimum, minimum.ZoomWithWheel(-120));
        Assert.IsTrue(minimum.ZoomWithWheel(120).Zoom > minimum.Zoom);
        Assert.AreSame(original, original.ZoomWithWheel(0));
        Assert.AreSame(CanvasViewportState.Empty, CanvasViewportState.Empty.ZoomWithWheel(120));
    }

    [TestMethod]
    public void ToSourceBounds_NormalizesReverseDragAndAppliesZoom()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(2);

        RectD sourceBounds = viewport.ToSourceBounds(new RectD(300, 220, -100, -80));

        Assert.AreEqual(new RectD(100, 70, 50, 40), sourceBounds);
    }

    [TestMethod]
    public void ToSourceBounds_ClampsToSourceImage()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(0.5);

        RectD sourceBounds = viewport.ToSourceBounds(new RectD(-50, -20, 600, 500));

        Assert.AreEqual(new RectD(0, 0, 800, 600), sourceBounds);
    }

    [TestMethod]
    public void ToSourcePoint_AppliesZoomAndClampsToSourceImage()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(2);

        PointD sourcePoint = viewport.ToSourcePoint(new PointD(1900, 100));

        Assert.AreEqual(new PointD(800, 50), sourcePoint);
    }

    [TestMethod]
    public void ToDisplayBounds_NormalizesSourceBoundsAndAppliesZoom()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(0.5);

        RectD displayBounds = viewport.ToDisplayBounds(new RectD(300, 240, -100, -80));

        Assert.AreEqual(new RectD(100, 80, 50, 40), displayBounds);
    }
}
