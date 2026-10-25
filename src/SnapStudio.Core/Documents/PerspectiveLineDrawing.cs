using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Documents;

public enum PerspectiveLineFamily { Horizontal, Vertical, FirstVanishingPoint, SecondVanishingPoint, ThirdVanishingPoint, CurvedHorizontal, CurvedVertical, CenterVanishingPoint }

public static class PerspectiveLineDrawing
{
    public const double LockDistance = 4;

    // A ruler passes through the clicked point. Its direction is continuous, independent of displayed grid density.
    public static PerspectiveDrawingSnapResult Constrain(PointD start, PointD end, RectD view, PerspectiveGridSettings settings,
        double scaleX = 1, double scaleY = 1, PerspectiveLineFamily? locked = null)
    {
        var free = new PerspectiveDrawingSnapResult(start, end, []);
        if (!new[] { start.X, start.Y, end.X, end.Y, view.X, view.Y, view.Width, view.Height, scaleX, scaleY }.All(double.IsFinite)
            || scaleX <= 0 || scaleY <= 0 || view.Width <= 0 || view.Height <= 0
            || start.X < view.X || start.X > view.Right || start.Y < view.Y || start.Y > view.Bottom) return free;
        PointD delta = new(end.X - start.X, end.Y - start.Y);
        if (locked is null && Math.Sqrt(Math.Pow(delta.X * scaleX, 2) + Math.Pow(delta.Y * scaleY, 2)) < LockDistance) return free;
        var s = settings.Normalize();
        double cx = view.X + view.Width * s.CenterX / 100, cy = view.Y + view.Height * s.Horizon / 100;
        double radius = Math.Min(view.Width, view.Height) * s.Radius / 100;
        var candidates = new List<(PerspectiveLineFamily Family, PointD Direction)>();
        void Add(PerspectiveLineFamily family, PointD d)
        {
            double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (double.IsFinite(length) && length > 1e-10) candidates.Add((family, new(d.X / length, d.Y / length)));
        }
        void Vanish(PerspectiveLineFamily family, double x, double y)
        {
            PointD d = new(x - start.X, y - start.Y);
            Add(family, Math.Abs(d.X) + Math.Abs(d.Y) < 1e-9 ? delta : d);
        }
        void Tangent(PerspectiveLineFamily family, bool horizontal)
        {
            double x = (start.X - cx) / radius, y = (start.Y - cy) / radius;
            if (x * x + y * y > 1 + 1e-8) return;
            if (horizontal) (x, y) = (y, x);
            PointD d;
            if (Math.Abs(x) < 1e-9) d = Math.Abs(Math.Abs(y) - 1) < 1e-8 ? (horizontal ? new(delta.Y, delta.X) : delta) : new(0, 1);
            else { double a = (x * x + y * y - 1) / (2 * x); d = new(-y, x - a); }
            Add(family, horizontal ? new(d.Y, d.X) : d);
        }
        switch (s.Mode)
        {
            case CanvasGridMode.OnePoint:
                Add(PerspectiveLineFamily.Horizontal, new(1, 0)); Add(PerspectiveLineFamily.Vertical, new(0, 1));
                Vanish(PerspectiveLineFamily.FirstVanishingPoint, cx, cy); break;
            case CanvasGridMode.TwoPoint:
            case CanvasGridMode.ThreePoint:
            case CanvasGridMode.FourPoint:
                Vanish(PerspectiveLineFamily.FirstVanishingPoint, view.X + view.Width * s.Left / 100, cy);
                Vanish(PerspectiveLineFamily.SecondVanishingPoint, view.X + view.Width * s.Right / 100, cy);
                if (s.Mode == CanvasGridMode.TwoPoint) Add(PerspectiveLineFamily.Vertical, new(0, 1));
                else if (s.Mode == CanvasGridMode.ThreePoint) Vanish(PerspectiveLineFamily.ThirdVanishingPoint, cx, view.Y + view.Height * s.Vertical / 100);
                else Tangent(PerspectiveLineFamily.CurvedVertical, false);
                break;
            case CanvasGridMode.FivePoint:
                Tangent(PerspectiveLineFamily.CurvedVertical, false); Tangent(PerspectiveLineFamily.CurvedHorizontal, true);
                Vanish(PerspectiveLineFamily.CenterVanishingPoint, cx, cy); break;
        }
        if (candidates.Any(c => c.Family == locked)) candidates = candidates.Where(c => c.Family == locked).ToList();
        double best = double.PositiveInfinity; PointD chosen = default, projected = end; PerspectiveLineFamily? selected = null;
        foreach (var candidate in candidates)
        {
            var d = candidate.Direction;
            double amount = (delta.X * d.X * scaleX * scaleX + delta.Y * d.Y * scaleY * scaleY)
                / (d.X * d.X * scaleX * scaleX + d.Y * d.Y * scaleY * scaleY);
            PointD p = new(start.X + d.X * amount, start.Y + d.Y * amount);
            double score = Math.Pow((p.X - end.X) * scaleX, 2) + Math.Pow((p.Y - end.Y) * scaleY, 2);
            if (score >= best - 1e-8) continue;
            best = score; chosen = d; projected = p; selected = candidate.Family;
        }
        if (selected is null) return free;
        var clipped = AnnotationBoundsEditor.ClipLine(new(start.X, start.Y, projected.X - start.X, projected.Y - start.Y), view);
        if (clipped is not RectD stroke) return free;
        double reach = (view.Width + view.Height) * 2;
        var guide = AnnotationBoundsEditor.ClipLine(new(start.X - chosen.X * reach, start.Y - chosen.Y * reach,
            chosen.X * reach * 2, chosen.Y * reach * 2), view);
        IReadOnlyList<AnnotationSnapGuide> guides = guide is RectD line
            ? [new(SnapGuideAxis.Vertical, 0, 0, 0, false, true, [new(line.X, line.Y), new(line.Right, line.Bottom)])] : [];
        return new(start, new(stroke.Right, stroke.Bottom), guides, selected);
    }
}
