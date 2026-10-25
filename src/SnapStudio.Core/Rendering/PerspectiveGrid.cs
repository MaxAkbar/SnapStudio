using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Rendering;

public enum CanvasGridMode { Rectangular, OnePoint, TwoPoint, ThreePoint, FourPoint, FivePoint }

// Positions are percentages of the viewBox/image, so the same workspace settings fit either editor.
public sealed record PerspectiveGridSettings(CanvasGridMode Mode = CanvasGridMode.Rectangular, double CenterX = 50,
    double Horizon = 50, double Left = -50, double Right = 150, double Vertical = -100, double Radius = 70, int Density = 12)
{
    public PerspectiveGridSettings Normalize() => new(Enum.IsDefined(Mode) ? Mode : CanvasGridMode.Rectangular,
        Bound(CenterX, 50, -300, 400), Bound(Horizon, 50, -300, 400), Bound(Left, -50, -500, 500),
        Bound(Right, 150, -500, 500), Bound(Vertical, -100, -500, 500), Bound(Radius, 70, 10, 300), Math.Clamp(Density, 4, 32));
    private static double Bound(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed record PerspectiveGridStroke(IReadOnlyList<PointD> Points, bool IsMajor);
public sealed record PerspectiveGridMatch(PointD Point, PerspectiveGridStroke Stroke, double Distance);

public static class PerspectiveGrid
{
    public static IReadOnlyList<PerspectiveGridStroke> Create(RectD view, PerspectiveGridSettings options)
    {
        if (!Finite(view.X, view.Y, view.Width, view.Height, view.Right, view.Bottom) || view.Width <= 0 || view.Height <= 0) return [];
        var s = options.Normalize();
        if (s.Mode == CanvasGridMode.Rectangular) return [];
        var strokes = new List<PerspectiveGridStroke>();
        double cx = view.X + view.Width * s.CenterX / 100, cy = view.Y + view.Height * s.Horizon / 100;
        double radius = Math.Min(view.Width, view.Height) * s.Radius / 100;
        void Curve(IReadOnlyList<PointD> points, bool major = false)
        {
            List<PointD> run = [];
            void Flush() { if (run.Count > 1) strokes.Add(new(run.ToArray(), major)); run = []; }
            for (int i = 1; i < points.Count; i++)
            {
                if (!Clip(points[i - 1], points[i], view, out PointD a, out PointD b)) { Flush(); continue; }
                if (run.Count > 0 && Distance(run[^1], a) > 0.000001) Flush();
                if (run.Count == 0) run.Add(a);
                run.Add(b);
            }
            Flush();
        }
        void Line(PointD a, PointD b, bool major = false) => Curve([a, b], major);
        void Fan(PointD point, bool circular = false)
        {
            double reach = Math.Max(view.Width, view.Height) * 20;
            for (int i = 0; i < s.Density * 2; i++)
            {
                double angle = i * Math.PI / (s.Density * 2), x = Math.Cos(angle), y = Math.Sin(angle);
                double length = circular ? radius : reach;
                Line(new(point.X - x * length, point.Y - y * length), new(point.X + x * length, point.Y + y * length), i % 4 == 0);
            }
        }
        void Arcs(bool horizontal)
        {
            for (int i = -s.Density; i <= s.Density; i++)
            {
                double t = (double)i / s.Density, a = t == 0 ? 0 : Math.Abs((t * t - 1) / (2 * t));
                var points = new List<PointD>();
                for (int j = 0; j <= 96; j++)
                {
                    double angle = -Math.PI / 2 + Math.PI * j / 96, y = Math.Sin(angle), c = Math.Cos(angle);
                    // Stable circle-through-three-points construction, including the straight center guide.
                    double x = t == 0 ? 0 : Math.Sign(t) * c * c / (Math.Sqrt(a * a + c * c) + a);
                    points.Add(horizontal ? new(cx + y * radius, cy + x * radius) : new(cx + x * radius, cy + y * radius));
                }
                Curve(points, i % 4 == 0);
            }
        }
        if (s.Mode == CanvasGridMode.OnePoint)
        {
            Fan(new(cx, cy));
            for (int i = 0; i <= s.Density; i++)
            {
                double x = view.X + view.Width * i / s.Density, y = view.Y + view.Height * i / s.Density;
                Line(new(x, view.Y), new(x, view.Bottom), i % 4 == 0);
                Line(new(view.X, y), new(view.Right, y), i % 4 == 0);
            }
        }
        else if (s.Mode is CanvasGridMode.TwoPoint or CanvasGridMode.ThreePoint or CanvasGridMode.FourPoint)
        {
            Fan(new(view.X + view.Width * s.Left / 100, cy));
            Fan(new(view.X + view.Width * s.Right / 100, cy));
            if (s.Mode == CanvasGridMode.ThreePoint) Fan(new(cx, view.Y + view.Height * s.Vertical / 100));
            else if (s.Mode == CanvasGridMode.FourPoint) Arcs(false);
            else for (int i = 0; i <= s.Density; i++)
            {
                double x = view.X + view.Width * i / s.Density;
                Line(new(x, view.Y), new(x, view.Bottom), i % 4 == 0);
            }
        }
        else { Arcs(false); Arcs(true); Fan(new(cx, cy), true); }
        return strokes;
    }

    // Rendering and snapping use the very same clipped segments, including curved guides.
    public static PerspectiveGridMatch? Find(PointD point, IReadOnlyList<PerspectiveGridStroke> strokes, double tolerance,
        PointD? direction = null, Func<PointD, bool>? feasible = null, double scaleX = 1, double scaleY = 1)
    {
        if (!Finite(point.X, point.Y, tolerance, scaleX, scaleY) || tolerance < 0 || scaleX <= 0 || scaleY <= 0) return null;
        PerspectiveGridMatch? best = null;
        foreach (var stroke in strokes)
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                PointD a = stroke.Points[i - 1], b = stroke.Points[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, t;
                PointD candidate;
                if (direction is PointD d)
                {
                    double cross = d.X * dy - d.Y * dx;
                    if (Math.Abs(cross) < 1e-10) continue;
                    t = ((a.X - point.X) * d.Y - (a.Y - point.Y) * d.X) / cross;
                    if (t < -1e-8 || t > 1 + 1e-8) continue;
                    candidate = new(a.X + t * dx, a.Y + t * dy);
                }
                else
                {
                    double length = dx * dx * scaleX * scaleX + dy * dy * scaleY * scaleY;
                    if (length < 1e-16) continue;
                    t = Math.Clamp(((point.X - a.X) * dx * scaleX * scaleX + (point.Y - a.Y) * dy * scaleY * scaleY) / length, 0, 1);
                    candidate = new(a.X + t * dx, a.Y + t * dy);
                }
                double distance = Math.Sqrt(Math.Pow((candidate.X - point.X) * scaleX, 2) + Math.Pow((candidate.Y - point.Y) * scaleY, 2));
                if (distance <= tolerance + 1e-8 && (best is null || distance < best.Distance - 1e-8) && (feasible?.Invoke(candidate) ?? true)) best = new(candidate, stroke, distance);
            }
        return best;
    }

    private static bool Clip(PointD a, PointD b, RectD r, out PointD from, out PointD to)
    {
        double low = 0, high = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Edge(double p, double q)
        {
            if (Math.Abs(p) < 1e-12) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > high) return false; low = Math.Max(low, t); }
            else { if (t < low) return false; high = Math.Min(high, t); }
            return true;
        }
        bool valid = Edge(-dx, a.X - r.X) && Edge(dx, r.Right - a.X) && Edge(-dy, a.Y - r.Y) && Edge(dy, r.Bottom - a.Y);
        from = new(a.X + low * dx, a.Y + low * dy); to = new(a.X + high * dx, a.Y + high * dy);
        return valid && Distance(from, to) > 1e-9;
    }
    private static bool Finite(params double[] values) => values.All(double.IsFinite);
    private static double Distance(PointD a, PointD b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
