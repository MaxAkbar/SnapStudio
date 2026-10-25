using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Documents;

public sealed record PerspectiveDrawingSnapResult(PointD Start, PointD End, IReadOnlyList<AnnotationSnapGuide> Guides);

public static class PerspectiveSnapping
{
    private static IReadOnlyList<AnnotationSnapGuide> Guide(PerspectiveGridMatch match) =>
        [new(SnapGuideAxis.Vertical, 0, 0, 0, false, true, match.Stroke.Points)];

    public static PerspectiveDrawingSnapResult Draw(PointD start, PointD end, AnnotationKind kind,
        IReadOnlyList<PerspectiveGridStroke> grid, SizeD canvas, double zoom, bool constrain = false)
    {
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) || !double.IsFinite(end.X) || !double.IsFinite(end.Y)) return new(start, end, []);
        PerspectiveGridMatch? Find(PointD point, PointD? direction = null) => PerspectiveGrid.Find(point, grid,
            AnnotationSnapping.DisplayTolerance, direction, p => p.X >= 0 && p.Y >= 0 && p.X <= canvas.Width && p.Y <= canvas.Height, zoom, zoom);
        var first = Find(start);
        PointD from = first?.Point ?? start, to = end;
        bool square = constrain && kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse;
        if (square)
        {
            double dx = end.X - from.X, dy = end.Y - from.Y, side = Math.Min(Math.Abs(dx), Math.Abs(dy));
            to = new(from.X + Math.Sign(dx) * side, from.Y + Math.Sign(dy) * side);
        }
        var last = Find(to, square ? new(to.X - from.X, to.Y - from.Y) : null);
        to = last?.Point ?? to;
        if (kind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            // A straight drawing guide should determine the line's direction even near guide crossings.
            double best = double.PositiveInfinity;
            foreach (var stroke in grid.Where(stroke => stroke.Points.Count == 2))
            {
                PerspectiveGridMatch? At(PointD point) => PerspectiveGrid.Find(point, [stroke], AnnotationSnapping.DisplayTolerance,
                    feasible: p => p.X >= 0 && p.Y >= 0 && p.X <= canvas.Width && p.Y <= canvas.Height, scaleX: zoom, scaleY: zoom);
                var a = At(start); if (a is null) continue;
                var b = At(end); if (b is null) continue;
                double distance = a.Distance * a.Distance + b.Distance * b.Distance;
                if (distance >= best) continue;
                best = distance; first = a; last = b; from = a.Point; to = b.Point;
            }
        }
        var guides = new List<AnnotationSnapGuide>();
        if (first is not null) guides.AddRange(Guide(first));
        if (last is not null && !ReferenceEquals(first?.Stroke, last.Stroke)) guides.AddRange(Guide(last));
        return new(from, to, guides);
    }

    public static AnnotationSnapResult Move(RectD proposed, IReadOnlyList<PerspectiveGridStroke> grid, SizeD canvas,
        double zoom, bool allowX = true, bool allowY = true)
    {
        if (!double.IsFinite(zoom) || zoom <= 0 || (!allowX && !allowY)) return new(proposed, []);
        PerspectiveGridMatch? best = null; PointD delta = default;
        PointD? direction = !allowX ? new(0, 1) : !allowY ? new(1, 0) : null;
        foreach (double x in new[] { proposed.X, proposed.X + proposed.Width / 2, proposed.Right })
            foreach (double y in new[] { proposed.Y, proposed.Y + proposed.Height / 2, proposed.Bottom })
            {
                var anchor = new PointD(x, y);
                var match = PerspectiveGrid.Find(anchor, grid, AnnotationSnapping.DisplayTolerance, direction,
                    p => proposed.X + p.X - x >= -1e-7 && proposed.Right + p.X - x <= canvas.Width + 1e-7
                        && proposed.Y + p.Y - y >= -1e-7 && proposed.Bottom + p.Y - y <= canvas.Height + 1e-7, zoom, zoom);
                if (match is not null && (best is null || match.Distance < best.Distance - 1e-8))
                { best = match; delta = new(match.Point.X - x, match.Point.Y - y); }
            }
        return best is null ? new(proposed, []) : new(proposed with { X = proposed.X + delta.X, Y = proposed.Y + delta.Y }, Guide(best));
    }

    public static AnnotationSnapResult Resize(RectD b, AnnotationBoundsHandle handle, IReadOnlyList<PerspectiveGridStroke> grid,
        SizeD canvas, double zoom, bool constrain = false)
    {
        if (!double.IsFinite(zoom) || zoom <= 0 || handle == AnnotationBoundsHandle.Move) return new(b, []);
        bool endpoint = handle is AnnotationBoundsHandle.Start or AnnotationBoundsHandle.End;
        bool left = handle is AnnotationBoundsHandle.Left or AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.BottomLeft;
        bool right = handle is AnnotationBoundsHandle.Right or AnnotationBoundsHandle.TopRight or AnnotationBoundsHandle.BottomRight;
        bool top = handle is AnnotationBoundsHandle.Top or AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.TopRight;
        bool bottom = handle is AnnotationBoundsHandle.Bottom or AnnotationBoundsHandle.BottomLeft or AnnotationBoundsHandle.BottomRight;
        PointD point = endpoint ? handle == AnnotationBoundsHandle.Start ? new(b.X, b.Y) : new(b.Right, b.Bottom)
            : new(left ? b.X : right ? b.Right : b.X + b.Width / 2, top ? b.Y : bottom ? b.Bottom : b.Y + b.Height / 2);
        PointD fixedPoint = endpoint ? handle == AnnotationBoundsHandle.Start ? new(b.Right, b.Bottom) : new(b.X, b.Y)
            : new(left ? b.Right : b.X, top ? b.Bottom : b.Y);
        PointD? direction = !endpoint && !(left || right) ? new(0, 1) : !endpoint && !(top || bottom) ? new(1, 0)
            : constrain ? new(point.X - fixedPoint.X, point.Y - fixedPoint.Y) : null;
        var match = PerspectiveGrid.Find(point, grid, AnnotationSnapping.DisplayTolerance, direction, p =>
            p.X >= 0 && p.Y >= 0 && p.X <= canvas.Width && p.Y <= canvas.Height && (endpoint ||
                ((!(left || right) || (left ? b.Right - p.X : p.X - b.X) >= Math.Min(AnnotationBoundsEditor.MinimumSize, canvas.Width))
                && (!(top || bottom) || (top ? b.Bottom - p.Y : p.Y - b.Y) >= Math.Min(AnnotationBoundsEditor.MinimumSize, canvas.Height)))), zoom, zoom);
        if (match is null) return new(b, []);
        if (endpoint) return new(AnnotationBoundsEditor.MoveEndpoint(b, handle, match.Point, canvas), Guide(match));
        double x = left ? match.Point.X : b.X, y = top ? match.Point.Y : b.Y;
        return new(new(x, y, (right ? match.Point.X : b.Right) - x, (bottom ? match.Point.Y : b.Bottom) - y), Guide(match));
    }

    // Object/canvas alignments win ties; otherwise use the closest visible guide in screen space.
    public static AnnotationSnapResult Closest(RectD proposed, AnnotationSnapResult objects, AnnotationSnapResult perspective)
    {
        if (perspective.Guides.Count == 0) return objects;
        if (objects.Guides.Count == 0) return perspective;
        static double Change(RectD a, RectD b) => Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Right - b.Right, 2) + Math.Pow(a.Bottom - b.Bottom, 2);
        return Change(proposed, perspective.Bounds) < Change(proposed, objects.Bounds) - 1e-8 ? perspective : objects;
    }
}
