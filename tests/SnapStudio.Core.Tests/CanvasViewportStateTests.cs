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
