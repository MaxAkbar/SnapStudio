namespace SnapStudio.Core.Rendering;

public sealed record CanvasGridLine(double Position, bool IsMajor);

public sealed record CanvasGridLayout(double SourceSpacing, double DisplaySpacing, int Stride)
{
    public const double DefaultSpacing = 32;
    public const double MinimumSpacing = 4;
    public const double MaximumSpacing = 512;
    public const double MinimumDisplaySpacing = 8;

    public static double NormalizeSpacing(double spacing) => double.IsFinite(spacing)
        ? Math.Clamp(Math.Round(spacing), MinimumSpacing, MaximumSpacing) : DefaultSpacing;

    public static CanvasGridLayout Create(double spacing, double zoom)
    {
        double normalized = NormalizeSpacing(spacing);
        if (!double.IsFinite(zoom) || zoom <= 0) return new(normalized, 0, 1);
        int stride = 1;
        // Only draw and snap to visible lines, keeping the same origin when zooming out.
        while (normalized * zoom * stride < MinimumDisplaySpacing && stride < 1_048_576) stride *= 2;
        return new(normalized * stride, normalized * zoom * stride, stride);
    }

    public IEnumerable<CanvasGridLine> Lines(double from, double to)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || DisplaySpacing <= 0 || !double.IsFinite(DisplaySpacing) || to < from) yield break;
        double first = Math.Ceiling(Math.Max(0, from) / DisplaySpacing), last = Math.Floor(to / DisplaySpacing);
        if (!double.IsFinite(first) || !double.IsFinite(last)) yield break;
        // Rendering asks for invalidated tiles, not an image-sized allocation.
        for (int offset = 0; offset < 100_000 && first + offset <= last; offset++)
        {
            double index = first + offset;
            yield return new(index * DisplaySpacing, index % 4 == 0);
        }
    }
}
