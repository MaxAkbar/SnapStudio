using System.Globalization;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Documents;

public enum PerspectiveRectanglePlane { Front, Side, Ground }

public sealed record PerspectiveRectangleGeometry(IReadOnlyList<PointD> Corners, IReadOnlyList<PointD> Outline, RectD Bounds)
{
    public AnnotationObject ToAnnotation(AnnotationStyle style)
    {
        static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string Color(ColorRgba color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        double pad = style.StrokeThickness / 2 + 1;
        RectD bounds = new(Bounds.X - pad, Bounds.Y - pad, Bounds.Width + 2 * pad, Bounds.Height + 2 * pad);
        string path = string.Join(" ", Outline.SkipLast(1).Select((p, i) => $"{(i == 0 ? "M" : "L")}{N(p.X)} {N(p.Y)}")) + " Z";
        double w = style.StrokeThickness;
        string dash = style.Pattern switch
        {
            StrokePattern.Dashed => $"{N(w * 4)} {N(w * 2)}",
            StrokePattern.Dotted => $"{N(w)} {N(w * 2)}",
            StrokePattern.DashDot => $"{N(w * 4)} {N(w * 2)} {N(w)} {N(w * 2)}",
            _ => "none"
        };
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{N(bounds.X)} {N(bounds.Y)} {N(bounds.Width)} {N(bounds.Height)}\" preserveAspectRatio=\"none\"><path d=\"{path}\" fill=\"{Color(style.Fill)}\" fill-opacity=\"{N(style.Fill.A / 255d)}\" stroke=\"{Color(style.Stroke)}\" stroke-opacity=\"{N(style.Stroke.A / 255d)}\" stroke-width=\"{N(w)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\" stroke-dasharray=\"{dash}\"/></svg>";
        return new() { Kind = AnnotationKind.Vector, Bounds = bounds, Style = style, Vector = new(svg, "Perspective rectangle") };
    }
}

public static class PerspectiveRectangle
{
    // Generalized circles also represent straight lines when A == 0.
    private readonly record struct Curve(double A, double B, double C, double D);
    private enum Family { Horizontal, Vertical, Left, Right, Depth, CurvedHorizontal, CurvedVertical, Third }

    public static PerspectiveRectangleGeometry? Create(PointD start, PointD end, RectD view,
        PerspectiveGridSettings settings, PerspectiveRectanglePlane plane = PerspectiveRectanglePlane.Side)
    {
        if (!new[] { start.X, start.Y, end.X, end.Y, view.X, view.Y, view.Right, view.Bottom }.All(double.IsFinite)
            || view.Width <= 0 || view.Height <= 0 || !Inside(start, view)) return null;
        end = new(Math.Clamp(end.X, view.X, view.Right), Math.Clamp(end.Y, view.Y, view.Bottom));
        var s = settings.Normalize();
        if (s.Mode == CanvasGridMode.Rectangular) return Flat(start, end);
        if (!Enum.IsDefined(plane)) plane = PerspectiveRectanglePlane.Side;
        double cx = view.X + view.Width * s.CenterX / 100, cy = view.Y + view.Height * s.Horizon / 100;
        double radius = Math.Min(view.Width, view.Height) * s.Radius / 100;
        PointD Local(PointD p) => new((p.X - cx) / radius, (p.Y - cy) / radius);
        PointD World(PointD p) => new(cx + p.X * radius, cy + p.Y * radius);
        (Family u, Family v) = s.Mode switch
        {
            CanvasGridMode.OnePoint => plane switch
            {
                PerspectiveRectanglePlane.Front => (Family.Horizontal, Family.Vertical),
                PerspectiveRectanglePlane.Ground => (Family.Horizontal, Family.Depth),
                _ => (Family.Depth, Family.Vertical)
            },
            CanvasGridMode.FivePoint => plane switch
            {
                PerspectiveRectanglePlane.Front => (Family.CurvedHorizontal, Family.CurvedVertical),
                PerspectiveRectanglePlane.Ground => (Family.CurvedHorizontal, Family.Depth),
                _ => (Family.Depth, Family.CurvedVertical)
            },
            _ => (plane == PerspectiveRectanglePlane.Side ? Family.Right : Family.Left,
                plane == PerspectiveRectanglePlane.Ground ? Family.Right : s.Mode == CanvasGridMode.ThreePoint ? Family.Third
                : s.Mode == CanvasGridMode.FourPoint ? Family.CurvedVertical : Family.Vertical)
        };
        bool curved = u is Family.CurvedHorizontal or Family.CurvedVertical || v is Family.CurvedHorizontal or Family.CurvedVertical;
        Curve Through(Family family, PointD p)
        {
            if (family == Family.CurvedVertical) return new(p.X, 1 - p.X * p.X - p.Y * p.Y, 0, -p.X);
            if (family == Family.CurvedHorizontal) return new(p.Y, 0, 1 - p.X * p.X - p.Y * p.Y, -p.Y);
            if (family == Family.Horizontal) return new(0, 0, 1, -p.Y);
            if (family == Family.Vertical) return new(0, 1, 0, -p.X);
            PointD target = family switch
            {
                Family.Left => Local(new(view.X + view.Width * s.Left / 100, cy)),
                Family.Right => Local(new(view.X + view.Width * s.Right / 100, cy)),
                Family.Third => Local(new(cx, view.Y + view.Height * s.Vertical / 100)),
                _ => new(0, 0)
            };
            double dx = target.X - p.X, dy = target.Y - p.Y;
            return new(0, -dy, dx, dy * p.X - dx * p.Y);
        }
        PointD a = Local(start);
        if (curved && a.X * a.X + a.Y * a.Y >= 1 - 1e-9) return null;
        PerspectiveRectangleGeometry? Build(double t)
        {
            PointD c = Local(new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t));
            if (curved && c.X * c.X + c.Y * c.Y > 1 + 1e-9) return null;
            Curve au = Through(u, a), av = Through(v, a), cu = Through(u, c), cv = Through(v, c);
            PointD? b = Intersect(au, cv, new(c.X, a.Y), curved), d = Intersect(av, cu, new(a.X, c.Y), curved);
            if (b is null || d is null) return null;
            PointD[] local = [a, b.Value, c, d.Value];
            double sign = 0;
            for (int i = 0; i < 4; i++)
            {
                var p = local[i]; var q = local[(i + 1) % 4]; var r = local[(i + 2) % 4];
                double cross = (q.X - p.X) * (r.Y - q.Y) - (q.Y - p.Y) * (r.X - q.X);
                if (!double.IsFinite(cross) || Math.Abs(cross) < 1e-10 || (sign != 0 && Math.Sign(cross) != sign)) return null;
                sign = Math.Sign(cross);
            }
            Curve[] edges = [au, cv, cu, av];
            var outline = new List<PointD>();
            for (int i = 0; i < 4; i++) outline.AddRange(Trace(edges[i], local[i], local[(i + 1) % 4]).Skip(i == 0 ? 0 : 1).Select(World));
            if (outline.Any(p => !Inside(p, view) || curved && (Math.Pow((p.X - cx) / radius, 2) + Math.Pow((p.Y - cy) / radius, 2) > 1 + 1e-8))) return null;
            PointD[] corners = local.Select(World).ToArray();
            double minX = outline.Min(p => p.X), minY = outline.Min(p => p.Y), maxX = outline.Max(p => p.X), maxY = outline.Max(p => p.Y);
            return new(corners, outline, new(minX, minY, maxX - minX, maxY - minY));
        }
        var full = Build(1); if (full is not null) return full;
        // Stop at the canvas, curved-grid rim or vanishing axis without independently clamping corners.
        double high = 1, low = .5;
        PerspectiveRectangleGeometry? best = null;
        for (int i = 0; i < 20 && best is null; i++) { best = Build(low); if (best is null) { high = low; low /= 2; } }
        if (best is null) return null;
        for (int i = 0; i < 24; i++) { double mid = (low + high) / 2; var candidate = Build(mid); if (candidate is null) high = mid; else { low = mid; best = candidate; } }
        return best;
    }

    public static PerspectiveRectangleGeometry? Flat(PointD a, PointD c)
    {
        if (!new[] { a.X, a.Y, c.X, c.Y }.All(double.IsFinite) || Math.Abs(a.X - c.X) < 1e-6 || Math.Abs(a.Y - c.Y) < 1e-6) return null;
        PointD[] corners = [a, new(c.X, a.Y), c, new(a.X, c.Y)];
        return new(corners, [.. corners, a], new(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Abs(c.X - a.X), Math.Abs(c.Y - a.Y)));
    }

    private static bool Inside(PointD p, RectD view) => double.IsFinite(p.X) && double.IsFinite(p.Y)
        && p.X >= view.X - 1e-7 && p.X <= view.Right + 1e-7 && p.Y >= view.Y - 1e-7 && p.Y <= view.Bottom + 1e-7;

    private static PointD? Intersect(Curve f, Curve g, PointD expected, bool disk)
    {
        double b, c, d;
        if (Math.Abs(f.A) < 1e-12 && Math.Abs(g.A) < 1e-12)
        {
            double det = f.B * g.C - g.B * f.C;
            if (Math.Abs(det) < 1e-12) return null;
            return new((f.C * g.D - g.C * f.D) / det, (g.B * f.D - f.B * g.D) / det);
        }
        if (Math.Abs(f.A) < 1e-12) { b = f.B; c = f.C; d = f.D; f = g; }
        else if (Math.Abs(g.A) < 1e-12) { b = g.B; c = g.C; d = g.D; }
        else { b = g.A * f.B - f.A * g.B; c = g.A * f.C - f.A * g.C; d = g.A * f.D - f.A * g.D; }
        double length = Math.Sqrt(b * b + c * c); if (length < 1e-12) return null;
        b /= length; c /= length; d /= length;
        PointD q = new(-d * b, -d * c), dir = new(-c, b);
        double bb = f.B * dir.X + f.C * dir.Y, cc = f.A * (q.X * q.X + q.Y * q.Y) + f.B * q.X + f.C * q.Y + f.D;
        double discriminant = bb * bb - 4 * f.A * cc; if (discriminant < -1e-10) return null;
        double root = Math.Sqrt(Math.Max(0, discriminant));
        return new[] { (-bb - root) / (2 * f.A), (-bb + root) / (2 * f.A) }
            .Select(t => new PointD(q.X + dir.X * t, q.Y + dir.Y * t))
            .Where(p => !disk || p.X * p.X + p.Y * p.Y <= 1 + 1e-8)
            .OrderBy(p => Math.Pow(p.X - expected.X, 2) + Math.Pow(p.Y - expected.Y, 2)).Select(p => (PointD?)p).FirstOrDefault();
    }

    private static IEnumerable<PointD> Trace(Curve curve, PointD from, PointD to)
    {
        if (Math.Abs(curve.A) < 1e-8) return [from, to];
        double x = -curve.B / (2 * curve.A), y = -curve.C / (2 * curve.A), r = Math.Sqrt((from.X - x) * (from.X - x) + (from.Y - y) * (from.Y - y));
        double start = Math.Atan2(from.Y - y, from.X - x), delta = Math.IEEERemainder(Math.Atan2(to.Y - y, to.X - x) - start, Math.PI * 2);
        int count = Math.Max(2, (int)Math.Ceiling(Math.Abs(delta) * 96 / Math.PI));
        var points = new List<PointD> { from };
        for (int i = 1; i < count; i++) { double angle = start + delta * i / count; points.Add(new(x + r * Math.Cos(angle), y + r * Math.Sin(angle))); }
        points.Add(to); return points;
    }
}
