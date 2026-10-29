using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Rendering;

public enum PerspectiveGuideHandleKind { Horizon, Center, Left, Right, Top, Bottom }
public sealed record PerspectiveGuideHandle(PerspectiveGuideHandleKind Kind, GridGuideFamily Family, PointD Position);

public static class PerspectiveGuideEditing
{
    public static IReadOnlyList<PerspectiveGuideHandle> Handles(SizeD image, PerspectiveGridSettings settings)
    {
        if (!Valid(image)) return [];
        var s = settings.Normalize();
        double x = image.Width * s.CenterX / 100, y = image.Height * s.Horizon / 100;
        double radius = Math.Min(image.Width, image.Height) * s.Radius / 100;
        return CanvasGridFamilies.Active(s.Mode).Where(f => f != GridGuideFamily.Grid).Select(f => new PerspectiveGuideHandle(
            Enum.Parse<PerspectiveGuideHandleKind>(f.ToString()), f, f switch
            {
                GridGuideFamily.Left => new(s.Mode == CanvasGridMode.FivePoint ? x - radius : image.Width * s.Left / 100, y),
                GridGuideFamily.Right => new(s.Mode == CanvasGridMode.FivePoint ? x + radius : image.Width * s.Right / 100, y),
                GridGuideFamily.Top => new(x, s.Mode == CanvasGridMode.ThreePoint ? image.Height * s.Vertical / 100 : y - radius),
                GridGuideFamily.Bottom => new(x, y + radius),
                _ => new(x, y)
            })).ToArray();
    }

    // Delta is measured from pointer-down against the original settings, including when a handle is outside the image.
    public static PerspectiveGridSettings Move(PerspectiveGridSettings original, SizeD image, PerspectiveGuideHandleKind handle, PointD delta)
    {
        var s = original.Normalize();
        if (!Valid(image) || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y) || s.Mode == CanvasGridMode.Rectangular) return s;
        if (handle != PerspectiveGuideHandleKind.Horizon && !Handles(image, s).Any(h => h.Kind == handle)) return s;
        double x = delta.X * 100 / image.Width, y = delta.Y * 100 / image.Height;
        double rx = delta.X * 100 / Math.Min(image.Width, image.Height), ry = delta.Y * 100 / Math.Min(image.Width, image.Height);
        return (handle switch
        {
            PerspectiveGuideHandleKind.Horizon => s with { Horizon = s.Horizon + y },
            PerspectiveGuideHandleKind.Center => s with { CenterX = s.CenterX + x, Horizon = s.Horizon + y },
            PerspectiveGuideHandleKind.Left when s.Mode == CanvasGridMode.FivePoint => s with { Radius = s.Radius - rx },
            PerspectiveGuideHandleKind.Right when s.Mode == CanvasGridMode.FivePoint => s with { Radius = s.Radius + rx },
            PerspectiveGuideHandleKind.Left => s with { Left = s.Left + x },
            PerspectiveGuideHandleKind.Right => s with { Right = s.Right + x },
            PerspectiveGuideHandleKind.Top when s.Mode == CanvasGridMode.ThreePoint => s with { CenterX = s.CenterX + x, Vertical = s.Vertical + y },
            PerspectiveGuideHandleKind.Top => s with { Radius = s.Radius - ry },
            PerspectiveGuideHandleKind.Bottom => s with { Radius = s.Radius + ry },
            _ => s
        }).Normalize();
    }

    public static string Title(GridGuideFamily family, CanvasGridMode mode) => family switch
    {
        GridGuideFamily.Grid => "Base grid",
        GridGuideFamily.Center => mode == CanvasGridMode.OnePoint ? "VP 1" : "Center VP",
        GridGuideFamily.Top => mode == CanvasGridMode.ThreePoint ? "Third VP" : "Top VP",
        _ => family + " VP"
    };

    private static bool Valid(SizeD size) => double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0 && size.Height > 0;
}
