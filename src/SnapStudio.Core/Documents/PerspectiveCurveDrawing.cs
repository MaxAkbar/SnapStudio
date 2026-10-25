using System.Globalization;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Documents;

public sealed record PerspectiveCurveDrawingResult(IReadOnlyList<PointD> Points, IReadOnlyList<AnnotationSnapGuide> Guides,
    PerspectiveLineFamily? Direction = null)
{
    public AnnotationObject ToAnnotation(AnnotationStyle style)
    {
        if (Points.Count < 2 || Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new InvalidOperationException("A perspective line needs at least two finite points.");
        static string N(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);
        double pad = style.StrokeThickness / 2 + 1;
        double x = Points.Min(p => p.X) - pad, y = Points.Min(p => p.Y) - pad;
        var bounds = new RectD(x, y, Points.Max(p => p.X) + pad - x, Points.Max(p => p.Y) + pad - y);
        string path = string.Join(" ", Points.Select((p, i) => $"{(i == 0 ? "M" : "L")}{N(p.X)} {N(p.Y)}"));
        double w = style.StrokeThickness;
        string dash = style.Pattern switch
        {
            StrokePattern.Dashed => $"{N(w * 4)} {N(w * 2)}",
            StrokePattern.Dotted => $"{N(w)} {N(w * 2)}",
            StrokePattern.DashDot => $"{N(w * 4)} {N(w * 2)} {N(w)} {N(w * 2)}",
            _ => "none"
        };
        string color = $"#{style.Stroke.R:X2}{style.Stroke.G:X2}{style.Stroke.B:X2}";
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{N(bounds.X)} {N(bounds.Y)} {N(bounds.Width)} {N(bounds.Height)}\" preserveAspectRatio=\"none\"><path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-opacity=\"{N(style.Stroke.A / 255d)}\" stroke-width=\"{N(w)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-dasharray=\"{dash}\"/></svg>";
        return new() { Kind = AnnotationKind.Vector, Bounds = bounds, Style = style, Vector = new(svg, "Perspective line") };
    }
}

public static class PerspectiveCurveDrawing
{
    // Follow the continuous guide through the clicked point, including curves between displayed guides.
    // The end is the closest screen-space point on the connected visible guide; the start never moves.
    public static PerspectiveCurveDrawingResult Draw(PointD start, PointD end, RectD view, PerspectiveGridSettings settings,
        double scaleX = 1, double scaleY = 1, PerspectiveLineFamily? locked = null)
    {
        if (!new[] { start.X, start.Y, end.X, end.Y, view.X, view.Y, view.Right, view.Bottom, scaleX, scaleY }.All(double.IsFinite)
            || view.Width <= 0 || view.Height <= 0 || scaleX <= 0 || scaleY <= 0 || !Inside(start, view)) return new([], []);
        var ruler = PerspectiveLineDrawing.Constrain(start, end, view, settings, scaleX, scaleY, locked);
        var straight = new PerspectiveCurveDrawingResult([ruler.Start, ruler.End], ruler.Guides, ruler.Direction);
        if (ruler.Direction is not (PerspectiveLineFamily.CurvedHorizontal or PerspectiveLineFamily.CurvedVertical)) return straight;
        var s = settings.Normalize();
        double cx = view.X + view.Width * s.CenterX / 100, cy = view.Y + view.Height * s.Horizon / 100;
        double radius = Math.Min(view.Width, view.Height) * s.Radius / 100;
        bool horizontal = ruler.Direction == PerspectiveLineFamily.CurvedHorizontal;
        double u = (start.X - cx) / radius, v = (start.Y - cy) / radius;
        double uMin = (view.X - cx) / radius, uMax = (view.Right - cx) / radius;
        double vMin = (view.Y - cy) / radius, vMax = (view.Bottom - cy) / radius;
        if (horizontal) { (u, v) = (v, u); (uMin, vMin) = (vMin, uMin); (uMax, vMax) = (vMax, uMax); }
        // The center guide is a straight axis. At a pole the clicked point alone cannot define a circle.
        if (Math.Abs(u) < 1e-9 || u * u + v * v > 1 + 1e-8) return straight;
        double signedA = (u * u + v * v - 1) / (2 * u), a = Math.Abs(signedA), sign = Math.Sign(u);
        PointD At(double phi)
        {
            double cosine = Math.Cos(phi), c2 = cosine * cosine;
            double x = sign * c2 / (Math.Sqrt(a * a + c2) + a), y = Math.Sin(phi);
            return horizontal ? new(cx + y * radius, cy + x * radius) : new(cx + x * radius, cy + y * radius);
        }
        double from = Math.Asin(Math.Clamp(v, -1, 1));
        var roots = new List<double> { -Math.PI / 2, from, Math.PI / 2, 0 };
        foreach (double bound in new[] { vMin, vMax })
            if (bound is >= -1 and <= 1) roots.Add(Math.Asin(bound));
        foreach (double bound in new[] { uMin, uMax })
        {
            double square = 1 + 2 * signedA * bound - bound * bound;
            if (square is < 0 or > 1) continue;
            double angle = Math.Asin(Math.Sqrt(Math.Clamp(square, 0, 1)));
            roots.Add(-angle); roots.Add(angle);
        }
        double low = from, high = from;
        // Boundary roots isolate every possible exit, including a narrow exit/reentry near the arc midpoint.
        foreach (double next in roots.Where(p => p < from - 1e-12).OrderDescending())
        {
            if (!Inside(At((low + next) / 2), view)) break;
            low = next;
        }
        foreach (double next in roots.Where(p => p > from + 1e-12).Order())
        {
            if (!Inside(At((high + next) / 2), view)) break;
            high = next;
        }
        double best = from, bestScore = double.PositiveInfinity;
        double Score(double phi)
        {
            PointD p = At(phi);
            double x = (p.X - end.X) * scaleX, y = (p.Y - end.Y) * scaleY;
            return x * x + y * y;
        }
        void Consider(double phi)
        {
            double score = Score(phi);
            if (score < bestScore - 1e-10 || Math.Abs(score - bestScore) <= 1e-10 && Math.Abs(phi - from) < Math.Abs(best - from))
            { best = phi; bestScore = score; }
        }
        Consider(from); Consider(low); Consider(high);
        // Restrict to the connected visible arc. Searching each short interval avoids choosing a distant
        // local minimum when nonuniform display scaling turns a circular guide into a screen ellipse.
        const double ratio = .6180339887498949;
        for (int segment = 0; segment < 64; segment++)
        {
            double left = low + (high - low) * segment / 64, right = low + (high - low) * (segment + 1) / 64;
            double c = right - ratio * (right - left), d = left + ratio * (right - left);
            double fc = Score(c), fd = Score(d);
            for (int step = 0; step < 48; step++)
            {
                if (fc < fd) { right = d; d = c; fd = fc; c = right - ratio * (right - left); fc = Score(c); }
                else { left = c; c = d; fc = fd; d = left + ratio * (right - left); fd = Score(d); }
            }
            Consider((left + right) / 2);
        }
        PointD[] Sample(double first, double last, bool exactStart)
        {
            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(last - first) * 96 / Math.PI));
            var points = new PointD[count + 1];
            for (int i = 0; i <= count; i++) points[i] = i == 0 && exactStart ? start : At(first + (last - first) * i / count);
            return points;
        }
        PointD[] guide = Sample(low, high, false);
        return new(Sample(from, best, true), [new(SnapGuideAxis.Vertical, 0, 0, 0, false, true, guide)], ruler.Direction);
    }

    private static bool Inside(PointD p, RectD view) => double.IsFinite(p.X) && double.IsFinite(p.Y)
        && p.X >= view.X - 1e-7 && p.X <= view.Right + 1e-7 && p.Y >= view.Y - 1e-7 && p.Y <= view.Bottom + 1e-7;
}
