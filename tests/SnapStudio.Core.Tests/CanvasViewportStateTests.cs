using SnapStudio.Core.Rendering;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class CanvasViewportStateTests
{
    [Fact]
    public void Create_WithInvalidSource_ReturnsEmptyViewport()
    {
        CanvasViewportState viewport = CanvasViewportState.Create(0, 600);

        Assert.False(viewport.HasSource);
        Assert.Equal(0, viewport.DisplayWidth);
        Assert.Equal(0, viewport.DisplayHeight);
    }

    [Fact]
    public void FitTo_UsesLimitingViewportDimension()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(1600, 900)
            .FitTo(800, 800);

        Assert.Equal(0.5, viewport.Zoom, precision: 3);
        Assert.Equal(800, viewport.DisplayWidth, precision: 3);
        Assert.Equal(450, viewport.DisplayHeight, precision: 3);
    }

    [Theory]
    [InlineData(0.01, CanvasViewportState.MinimumZoom)]
    [InlineData(100, CanvasViewportState.MaximumZoom)]
    [InlineData(double.NaN, 1)]
    public void WithZoom_ClampsInvalidZoom(double requestedZoom, double expectedZoom)
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(requestedZoom);

        Assert.Equal(expectedZoom, viewport.Zoom);
    }

    [Fact]
    public void ToSourceBounds_NormalizesReverseDragAndAppliesZoom()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(2);

        RectD sourceBounds = viewport.ToSourceBounds(new RectD(300, 220, -100, -80));

        Assert.Equal(new RectD(100, 70, 50, 40), sourceBounds);
    }

    [Fact]
    public void ToSourceBounds_ClampsToSourceImage()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(0.5);

        RectD sourceBounds = viewport.ToSourceBounds(new RectD(-50, -20, 600, 500));

        Assert.Equal(new RectD(0, 0, 800, 600), sourceBounds);
    }

    [Fact]
    public void ToSourcePoint_AppliesZoomAndClampsToSourceImage()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(2);

        PointD sourcePoint = viewport.ToSourcePoint(new PointD(1900, 100));

        Assert.Equal(new PointD(800, 50), sourcePoint);
    }

    [Fact]
    public void ToDisplayBounds_NormalizesSourceBoundsAndAppliesZoom()
    {
        CanvasViewportState viewport = CanvasViewportState
            .Create(800, 600)
            .WithZoom(0.5);

        RectD displayBounds = viewport.ToDisplayBounds(new RectD(300, 240, -100, -80));

        Assert.Equal(new RectD(100, 80, 50, 40), displayBounds);
    }
}
