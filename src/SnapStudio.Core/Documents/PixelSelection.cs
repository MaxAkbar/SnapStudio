using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public enum PixelSelectionTool { Rectangle, Ellipse, Lasso, MagicWand, PolygonalLasso }
public enum PixelSelectionMode { Replace, Add, Subtract, Intersect }
public readonly record struct PixelSpan(int Y, int Left, int Right);
public readonly record struct SelectionEdge(PointD Start, PointD End);

/// <summary>An immutable, source-pixel selection stored as disjoint horizontal runs.</summary>
public sealed class PixelSelection
{
    private readonly Dictionary<int, PixelSpan[]> _rows;
    private readonly PixelSpan[] _spans;

    private PixelSelection(int width, int height, IEnumerable<PixelSpan> spans)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _rows = spans.Where(s => s.Y >= 0 && s.Y < height)
            .Select(s => s with { Left = Math.Clamp(s.Left, 0, width), Right = Math.Clamp(s.Right, 0, width) })
            .Where(s => s.Right > s.Left).GroupBy(s => s.Y)
            .ToDictionary(g => g.Key, g => MergeRow(g.Key, g));
        _spans = _rows.OrderBy(row => row.Key).SelectMany(row => row.Value).ToArray();
        Spans = Array.AsReadOnly(_spans);
        if (_spans.Length > 0)
        {
            int left = _spans.Min(s => s.Left), right = _spans.Max(s => s.Right);
            Bounds = new(left, _spans[0].Y, right - left, _spans[^1].Y - _spans[0].Y + 1);
        }
    }

    public int Width { get; }
    public int Height { get; }
    public RectD? Bounds { get; }
    public bool IsEmpty => _spans.Length == 0;
    public IReadOnlyList<PixelSpan> Spans { get; }
    public long PixelCount => _spans.Sum(s => (long)s.Right - s.Left);
    public bool Contains(int x, int y) => _rows.TryGetValue(y, out PixelSpan[]? row) && row.Any(s => x >= s.Left && x < s.Right);

    public static PixelSelection Empty(int width, int height) => new(width, height, []);

    public static PixelSelection Create(int width, int height, PixelSelectionTool tool, RectD bounds, IReadOnlyList<PointD>? points = null)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
            throw new ArgumentException("Selection bounds must be finite.", nameof(bounds));
        int left = (int)Math.Clamp(Math.Floor(Math.Min(bounds.X, bounds.Right)), 0, width);
        int right = (int)Math.Clamp(Math.Ceiling(Math.Max(bounds.X, bounds.Right)), 0, width);
        int top = (int)Math.Clamp(Math.Floor(Math.Min(bounds.Y, bounds.Bottom)), 0, height);
        int bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(bounds.Y, bounds.Bottom)), 0, height);
        var spans = new List<PixelSpan>();
        if (tool is PixelSelectionTool.Lasso or PixelSelectionTool.PolygonalLasso)
        {
            if (points is null || points.Count < 3) return Empty(width, height);
            if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                throw new ArgumentException("Lasso points must be finite.", nameof(points));
            top = (int)Math.Clamp(Math.Floor(points.Min(p => p.Y)), 0, height);
            bottom = (int)Math.Clamp(Math.Ceiling(points.Max(p => p.Y)), 0, height);
            for (int y = top; y < bottom; y++)
            {
                var intersections = new List<double>();
                double scan = y + 0.5;
                for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                {
                    PointD a = points[j], b = points[i];
                    if ((a.Y <= scan && b.Y > scan) || (b.Y <= scan && a.Y > scan))
                        intersections.Add(a.X + (scan - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                    spans.Add(new(y, PixelBoundary(intersections[i], width), PixelBoundary(intersections[i + 1], width)));
            }
        }
        else if (tool == PixelSelectionTool.Ellipse && right > left && bottom > top)
        {
            double rx = (right - left) / 2d, ry = (bottom - top) / 2d;
            for (int y = top; y < bottom; y++)
            {
                double dy = (y + 0.5 - top - ry) / ry;
                double dx = rx * Math.Sqrt(Math.Max(0, 1 - dy * dy));
                spans.Add(new(y, PixelBoundary(left + rx - dx, width), PixelBoundary(left + rx + dx, width)));
            }
        }
        else if (tool == PixelSelectionTool.Rectangle)
            for (int y = top; y < bottom; y++) spans.Add(new(y, left, right));
        return new(width, height, spans);
    }

    private static int PixelBoundary(double coordinate, int maximum) => (int)Math.Clamp(Math.Ceiling(coordinate - 0.5), 0, maximum);

    public static PixelSelection FromRectangles(int width, int height, IEnumerable<RectD> rectangles)
    {
        var spans = new List<PixelSpan>();
        foreach (RectD rectangle in rectangles)
        {
            if (!double.IsFinite(rectangle.X) || !double.IsFinite(rectangle.Y) || !double.IsFinite(rectangle.Right) || !double.IsFinite(rectangle.Bottom))
                throw new ArgumentException("Selection rectangles must be finite.", nameof(rectangles));
            int top = (int)Math.Clamp(Math.Round(rectangle.Y), 0, height);
            int bottom = (int)Math.Clamp(Math.Round(rectangle.Bottom), 0, height);
            int left = (int)Math.Clamp(Math.Round(rectangle.X), 0, width);
            int right = (int)Math.Clamp(Math.Round(rectangle.Right), 0, width);
            for (int y = top; y < bottom; y++) spans.Add(new(y, left, right));
        }
        return new(width, height, spans);
    }

    public PixelSelection Combine(PixelSelection other, PixelSelectionMode mode)
    {
        if (Width != other.Width || Height != other.Height) throw new ArgumentException("Selection sizes must match.", nameof(other));
        if (mode == PixelSelectionMode.Replace) return other;
        return new(Width, Height, _rows.Keys.Union(other._rows.Keys).Order().SelectMany(y =>
            CombineRow(y, _rows.GetValueOrDefault(y) ?? [], other._rows.GetValueOrDefault(y) ?? [], mode)));
    }

    public PixelSelection Invert() => Create(Width, Height, PixelSelectionTool.Rectangle, new(0, 0, Width, Height)).Combine(this, PixelSelectionMode.Subtract);

    public PixelSelection Move(int dx, int dy)
    {
        if (Bounds is not RectD bounds) return this;
        dx = Math.Clamp(dx, -(int)bounds.X, Width - (int)bounds.Right);
        dy = Math.Clamp(dy, -(int)bounds.Y, Height - (int)bounds.Bottom);
        return new(Width, Height, _spans.Select(s => new PixelSpan(s.Y + dy, s.Left + dx, s.Right + dx)));
    }

    public IReadOnlyList<RectD> GetRectangles()
    {
        var rectangles = new List<RectD>();
        var last = new Dictionary<(int, int), int>();
        foreach (PixelSpan span in _spans)
        {
            var key = (span.Left, span.Right);
            if (last.TryGetValue(key, out int index) && rectangles[index].Bottom == span.Y)
                rectangles[index] = rectangles[index] with { Height = rectangles[index].Height + 1 };
            else
            {
                last[key] = rectangles.Count;
                rectangles.Add(new(span.Left, span.Y, span.Right - span.Left, 1));
            }
        }
        return rectangles.AsReadOnly();
    }

    public AnnotationObject CreateFill(ColorRgba color)
    {
        if (Bounds is not RectD bounds) throw new InvalidOperationException("Select pixels before filling.");
        return new AnnotationObject
        {
            Kind = AnnotationKind.Fill,
            Bounds = bounds,
            FillRectangles = GetRectangles().Select(r => new RectD((r.X - bounds.X) / bounds.Width,
                (r.Y - bounds.Y) / bounds.Height, r.Width / bounds.Width, r.Height / bounds.Height)).ToList(),
            Style = new(ColorRgba.Transparent, color, ColorRgba.Black, 1, 1)
        };
    }

    public IEnumerable<SelectionEdge> GetEdges()
    {
        // Subtract neighboring runs so holes and disjoint islands have their own boundary.
        var vertical = new Dictionary<int, List<(int Top, int Bottom)>>();
        foreach (var (y, row) in _rows.OrderBy(row => row.Key))
        {
            foreach (PixelSpan span in CombineRow(y, row, _rows.GetValueOrDefault(y - 1) ?? [], PixelSelectionMode.Subtract))
                yield return new(new(span.Left, y), new(span.Right, y));
            foreach (PixelSpan span in CombineRow(y, row, _rows.GetValueOrDefault(y + 1) ?? [], PixelSelectionMode.Subtract))
                yield return new(new(span.Left, y + 1), new(span.Right, y + 1));
            foreach (int x in row.SelectMany(s => new[] { s.Left, s.Right }))
            {
                if (!vertical.TryGetValue(x, out var runs)) vertical[x] = runs = [];
                if (runs.Count > 0 && runs[^1].Bottom == y) runs[^1] = (runs[^1].Top, y + 1);
                else runs.Add((y, y + 1));
            }
        }
        foreach (var (x, runs) in vertical)
            foreach (var (top, bottom) in runs) yield return new(new(x, top), new(x, bottom));
    }

    private static PixelSpan[] MergeRow(int y, IEnumerable<PixelSpan> source)
    {
        var result = new List<PixelSpan>();
        foreach (PixelSpan span in source.OrderBy(s => s.Left))
        {
            if (result.Count > 0 && span.Left <= result[^1].Right)
                result[^1] = result[^1] with { Right = Math.Max(result[^1].Right, span.Right) };
            else result.Add(span with { Y = y });
        }
        return result.ToArray();
    }

    private static IEnumerable<PixelSpan> CombineRow(int y, PixelSpan[] first, PixelSpan[] second, PixelSelectionMode mode)
    {
        var events = new SortedDictionary<int, (int A, int B)>();
        void Add(int x, int a, int b)
        {
            var current = events.GetValueOrDefault(x);
            events[x] = (current.A + a, current.B + b);
        }
        foreach (PixelSpan span in first) { Add(span.Left, 1, 0); Add(span.Right, -1, 0); }
        foreach (PixelSpan span in second) { Add(span.Left, 0, 1); Add(span.Right, 0, -1); }
        int a = 0, b = 0, start = -1;
        foreach (var (x, delta) in events)
        {
            a += delta.A; b += delta.B;
            bool inside = mode switch { PixelSelectionMode.Add => a > 0 || b > 0, PixelSelectionMode.Subtract => a > 0 && b == 0, _ => a > 0 && b > 0 };
            if (inside && start < 0) start = x;
            else if (!inside && start >= 0) { yield return new(y, start, x); start = -1; }
        }
    }
}
