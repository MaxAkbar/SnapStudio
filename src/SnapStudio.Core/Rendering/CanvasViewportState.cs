using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Rendering;

public sealed record CanvasViewportState(
    int SourceWidth,
    int SourceHeight,
    double Zoom)
{
    public const double MinimumZoom = 0.1;
    public const double MaximumZoom = 8.0;

    public static CanvasViewportState Empty { get; } = new(0, 0, 1);

    public bool HasSource => SourceWidth > 0 && SourceHeight > 0;

    public double DisplayWidth => HasSource ? SourceWidth * Zoom : 0;

    public double DisplayHeight => HasSource ? SourceHeight * Zoom : 0;

    public static CanvasViewportState Create(int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return Empty;
        }

        return new CanvasViewportState(sourceWidth, sourceHeight, 1);
    }

    public CanvasViewportState WithZoom(double zoom)
    {
        if (!HasSource)
        {
            return Empty;
        }

        return this with { Zoom = ClampZoom(zoom) };
    }

    public CanvasViewportState FitTo(double viewportWidth, double viewportHeight)
    {
        if (!HasSource || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return this;
        }

        double fitZoom = Math.Min(
            viewportWidth / SourceWidth,
            viewportHeight / SourceHeight);

        return WithZoom(fitZoom);
    }

    public RectD ToSourceBounds(RectD displayBounds)
    {
        if (!HasSource)
        {
            return new RectD(0, 0, 0, 0);
        }

        RectD normalized = Normalize(displayBounds);
        double left = Math.Clamp(normalized.X / Zoom, 0, SourceWidth);
        double top = Math.Clamp(normalized.Y / Zoom, 0, SourceHeight);
        double right = Math.Clamp(normalized.Right / Zoom, left, SourceWidth);
        double bottom = Math.Clamp(normalized.Bottom / Zoom, top, SourceHeight);

        return new RectD(left, top, right - left, bottom - top);
    }

    public PointD ToSourcePoint(PointD displayPoint)
    {
        if (!HasSource)
        {
            return new PointD(0, 0);
        }

        return new PointD(
            Math.Clamp(displayPoint.X / Zoom, 0, SourceWidth),
            Math.Clamp(displayPoint.Y / Zoom, 0, SourceHeight));
    }

    public RectD ToDisplayBounds(RectD sourceBounds)
    {
        RectD normalized = Normalize(sourceBounds);

        return new RectD(
            normalized.X * Zoom,
            normalized.Y * Zoom,
            normalized.Width * Zoom,
            normalized.Height * Zoom);
    }

    public static double ClampZoom(double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom))
        {
            return 1;
        }

        return Math.Clamp(zoom, MinimumZoom, MaximumZoom);
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }
}
