using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public enum SnapGuideAxis { Vertical, Horizontal }
public sealed record AnnotationSnapGuide(SnapGuideAxis Axis, double Position, double From, double To, bool IsCenter, bool IsGrid = false);
public sealed record AnnotationSnapResult(RectD Bounds, IReadOnlyList<AnnotationSnapGuide> Guides);

public static class AnnotationSnapping
{
    public const double DisplayTolerance = 6;
    private const double Epsilon = 0.000001;
    private sealed record Candidate(double Position, double Delta, int MovingAnchor, int TargetAnchor, RectD Target, bool Canvas, bool Grid = false);

    public static IReadOnlyList<RectD> GetTargets(CaptureDocument document, IReadOnlySet<Guid> excluded) => document.Annotations
        .Where(item => !excluded.Contains(item.Id) && DocumentLayers.IsEffectivelyVisible(document, item) && Finite(item.Bounds))
        .Select(item => Normalize(item.Bounds)).OrderBy(bounds => bounds.X).ThenBy(bounds => bounds.Y)
        .ThenBy(bounds => bounds.Width).ThenBy(bounds => bounds.Height).ToArray();

    public static AnnotationSnapResult Move(RectD proposed, IReadOnlyList<RectD> targets, SizeD canvas, double zoom,
        bool allowX = true, bool allowY = true, double gridSpacing = 0)
    {
        if (!Valid(proposed, canvas, zoom)) return new(proposed, []);
        RectD moving = Normalize(proposed);
        double tolerance = DisplayTolerance / zoom;
        Candidate? x = allowX ? Find(moving, targets, canvas, true, [0, 1, 2], tolerance,
            delta => delta >= Math.Min(0, -moving.X) - Epsilon && delta <= Math.Max(0, canvas.Width - moving.Right) + Epsilon, gridSpacing) : null;
        Candidate? y = allowY ? Find(moving, targets, canvas, false, [0, 1, 2], tolerance,
            delta => delta >= Math.Min(0, -moving.Y) - Epsilon && delta <= Math.Max(0, canvas.Height - moving.Bottom) + Epsilon, gridSpacing) : null;
        RectD result = proposed with { X = proposed.X + (x?.Delta ?? 0), Y = proposed.Y + (y?.Delta ?? 0) };
        return new(result, Guides(Normalize(result), x, y, canvas));
    }

    public static AnnotationSnapResult Resize(RectD proposed, AnnotationBoundsHandle handle, IReadOnlyList<RectD> targets,
        SizeD canvas, double zoom, bool square = false, double gridSpacing = 0)
    {
        if (!Valid(proposed, canvas, zoom) || handle == AnnotationBoundsHandle.Move) return new(proposed, []);
        if (handle is AnnotationBoundsHandle.Start or AnnotationBoundsHandle.End)
            return Endpoint(proposed, handle, targets, canvas, zoom, gridSpacing);
        RectD bounds = Normalize(proposed);
        int? xAnchor = handle is AnnotationBoundsHandle.Left or AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.BottomLeft ? 0
            : handle is AnnotationBoundsHandle.Right or AnnotationBoundsHandle.TopRight or AnnotationBoundsHandle.BottomRight ? 2 : null;
        int? yAnchor = handle is AnnotationBoundsHandle.Top or AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.TopRight ? 0
            : handle is AnnotationBoundsHandle.Bottom or AnnotationBoundsHandle.BottomLeft or AnnotationBoundsHandle.BottomRight ? 2 : null;
        if (square && xAnchor.HasValue && yAnchor.HasValue)
            return Square(bounds, xAnchor.Value, yAnchor.Value, targets, canvas, zoom, gridSpacing);
        double tolerance = DisplayTolerance / zoom;
        double minimumWidth = Math.Min(AnnotationBoundsEditor.MinimumSize, canvas.Width);
        double minimumHeight = Math.Min(AnnotationBoundsEditor.MinimumSize, canvas.Height);
        Candidate? x = xAnchor is int xa ? Find(bounds, targets, canvas, true, [xa], tolerance, delta =>
        {
            double edge = Anchor(bounds, true, xa) + delta;
            return edge >= 0 && edge <= canvas.Width && (xa == 0 ? bounds.Right - edge : edge - bounds.X) >= minimumWidth;
        }, gridSpacing) : null;
        Candidate? y = yAnchor is int ya ? Find(bounds, targets, canvas, false, [ya], tolerance, delta =>
        {
            double edge = Anchor(bounds, false, ya) + delta;
            return edge >= 0 && edge <= canvas.Height && (ya == 0 ? bounds.Bottom - edge : edge - bounds.Y) >= minimumHeight;
        }, gridSpacing) : null;
        double left = bounds.X + (xAnchor == 0 ? x?.Delta ?? 0 : 0), right = bounds.Right + (xAnchor == 2 ? x?.Delta ?? 0 : 0);
        double top = bounds.Y + (yAnchor == 0 ? y?.Delta ?? 0 : 0), bottom = bounds.Bottom + (yAnchor == 2 ? y?.Delta ?? 0 : 0);
        RectD result = new(left, top, right - left, bottom - top);
        return new(result, Guides(result, x, y, canvas));
    }

    private static AnnotationSnapResult Endpoint(RectD proposed, AnnotationBoundsHandle handle, IReadOnlyList<RectD> targets, SizeD canvas, double zoom, double gridSpacing)
    {
        PointD point = handle == AnnotationBoundsHandle.Start ? new(proposed.X, proposed.Y) : new(proposed.Right, proposed.Bottom);
        var endpoint = new RectD(point.X, point.Y, 0, 0);
        Candidate? x = Find(endpoint, targets, canvas, true, [0], DisplayTolerance / zoom, delta => point.X + delta >= 0 && point.X + delta <= canvas.Width, gridSpacing);
        Candidate? y = Find(endpoint, targets, canvas, false, [0], DisplayTolerance / zoom, delta => point.Y + delta >= 0 && point.Y + delta <= canvas.Height, gridSpacing);
        RectD result = AnnotationBoundsEditor.MoveEndpoint(proposed, handle, new(point.X + (x?.Delta ?? 0), point.Y + (y?.Delta ?? 0)), canvas);
        return new(result, Guides(Normalize(result), x, y, canvas));
    }

    private static AnnotationSnapResult Square(RectD bounds, int xa, int ya, IReadOnlyList<RectD> targets, SizeD canvas, double zoom, double gridSpacing)
    {
        double fixedX = xa == 0 ? bounds.Right : bounds.X, fixedY = ya == 0 ? bounds.Bottom : bounds.Y;
        double maxSide = Math.Min(xa == 0 ? fixedX : canvas.Width - fixedX, ya == 0 ? fixedY : canvas.Height - fixedY);
        double minimum = Math.Min(AnnotationBoundsEditor.MinimumSize, Math.Min(canvas.Width, canvas.Height));
        double Side(double delta, int anchor) => bounds.Width + (anchor == 0 ? -delta : delta);
        Candidate? x = Find(bounds, targets, canvas, true, [xa], DisplayTolerance / zoom,
            delta => Side(delta, xa) >= minimum && Side(delta, xa) <= maxSide, gridSpacing);
        Candidate? y = Find(bounds, targets, canvas, false, [ya], DisplayTolerance / zoom,
            delta => Side(delta, ya) >= minimum && Side(delta, ya) <= maxSide, gridSpacing);
        if (x is null && y is null) return new(bounds, []);
        double side = x is not null && (y is null || Math.Abs(x.Delta) <= Math.Abs(y.Delta)) ? Side(x.Delta, xa) : Side(y!.Delta, ya);
        RectD result = new(xa == 0 ? fixedX - side : fixedX, ya == 0 ? fixedY - side : fixedY, side, side);
        // A constrained corner has one degree of freedom; never show a second guide unless it really matches.
        if (x is not null && Math.Abs(Anchor(result, true, xa) - x.Position) > Epsilon) x = null;
        if (y is not null && Math.Abs(Anchor(result, false, ya) - y.Position) > Epsilon) y = null;
        return new(result, Guides(result, x, y, canvas));
    }

    private static Candidate? Find(RectD moving, IReadOnlyList<RectD> targets, SizeD canvas, bool vertical, int[] anchors,
        double tolerance, Func<double, bool> feasible, double gridSpacing)
    {
        Candidate? best = null;
        foreach (RectD target in targets.Where(Finite).Select(Normalize)
            .Where(target => target.Right >= 0 && target.X <= canvas.Width && target.Bottom >= 0 && target.Y <= canvas.Height)
            .Append(new(0, 0, canvas.Width, canvas.Height)))
        {
            bool isCanvas = target == new RectD(0, 0, canvas.Width, canvas.Height);
            foreach (int movingAnchor in anchors)
                for (int targetAnchor = 0; targetAnchor < 3; targetAnchor++)
                {
                    double position = Anchor(target, vertical, targetAnchor);
                    double delta = position - Anchor(moving, vertical, movingAnchor);
                    if (position < 0 || position > (vertical ? canvas.Width : canvas.Height) || Math.Abs(delta) > tolerance + Epsilon || !feasible(delta)) continue;
                    var candidate = new Candidate(position, delta, movingAnchor, targetAnchor, target, isCanvas);
                    if (best is null || Better(candidate, best)) best = candidate;
                }
        }
        if (double.IsFinite(gridSpacing) && gridSpacing > 0)
        {
            foreach (int movingAnchor in anchors)
            {
                double origin = Anchor(moving, vertical, movingAnchor);
                double cell = origin / gridSpacing;
                foreach (double index in new[] { Math.Floor(cell), Math.Ceiling(cell) })
                {
                    double position = index * gridSpacing, delta = position - origin;
                    if (!double.IsFinite(position) || position < 0 || position > (vertical ? canvas.Width : canvas.Height)
                        || Math.Abs(delta) > tolerance + Epsilon || !feasible(delta)) continue;
                    var candidate = new Candidate(position, delta, movingAnchor, -1, new(0, 0, canvas.Width, canvas.Height), true, true);
                    if (best is null || Better(candidate, best)) best = candidate;
                }
            }
        }
        return best;
    }

    private static bool Better(Candidate candidate, Candidate current)
    {
        double difference = Math.Abs(candidate.Delta) - Math.Abs(current.Delta);
        if (Math.Abs(difference) > Epsilon) return difference < 0;
        if (candidate.Grid != current.Grid) return !candidate.Grid;
        bool same = candidate.MovingAnchor == candidate.TargetAnchor, currentSame = current.MovingAnchor == current.TargetAnchor;
        if (same != currentSame) return same;
        if (candidate.Canvas != current.Canvas) return !candidate.Canvas;
        if (candidate.Position != current.Position) return candidate.Position < current.Position;
        return candidate.MovingAnchor < current.MovingAnchor;
    }

    private static IReadOnlyList<AnnotationSnapGuide> Guides(RectD bounds, Candidate? x, Candidate? y, SizeD canvas)
    {
        var guides = new List<AnnotationSnapGuide>(2);
        if (x is not null) guides.Add(new(SnapGuideAxis.Vertical, x.Position,
            x.Canvas ? 0 : Math.Clamp(Math.Min(bounds.Y, x.Target.Y), 0, canvas.Height),
            x.Canvas ? canvas.Height : Math.Clamp(Math.Max(bounds.Bottom, x.Target.Bottom), 0, canvas.Height), x.MovingAnchor == 1 && x.TargetAnchor == 1, x.Grid));
        if (y is not null) guides.Add(new(SnapGuideAxis.Horizontal, y.Position,
            y.Canvas ? 0 : Math.Clamp(Math.Min(bounds.X, y.Target.X), 0, canvas.Width),
            y.Canvas ? canvas.Width : Math.Clamp(Math.Max(bounds.Right, y.Target.Right), 0, canvas.Width), y.MovingAnchor == 1 && y.TargetAnchor == 1, y.Grid));
        return guides;
    }

    private static double Anchor(RectD bounds, bool vertical, int anchor) => vertical ? bounds.X + bounds.Width * anchor / 2 : bounds.Y + bounds.Height * anchor / 2;
    private static RectD Normalize(RectD bounds) => new(Math.Min(bounds.X, bounds.Right), Math.Min(bounds.Y, bounds.Bottom), Math.Abs(bounds.Width), Math.Abs(bounds.Height));
    private static bool Finite(RectD bounds) => double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width)
        && double.IsFinite(bounds.Height) && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);
    private static bool Valid(RectD bounds, SizeD canvas, double zoom) => Finite(bounds) && double.IsFinite(canvas.Width) && double.IsFinite(canvas.Height)
        && canvas.Width > 0 && canvas.Height > 0 && double.IsFinite(zoom) && zoom > 0;
}
