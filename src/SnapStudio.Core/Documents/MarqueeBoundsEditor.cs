using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class MarqueeBoundsEditor
{
    public static RectD Create(PointD anchor, PointD pointer, SizeD canvas, bool square = false, bool fromCenter = false)
    {
        double x = Math.Clamp(anchor.X, 0, canvas.Width), y = Math.Clamp(anchor.Y, 0, canvas.Height);
        double dx = Math.Clamp(pointer.X, 0, canvas.Width) - x, dy = Math.Clamp(pointer.Y, 0, canvas.Height) - y;
        double availableX = fromCenter ? Math.Min(x, canvas.Width - x) : dx < 0 ? x : canvas.Width - x;
        double availableY = fromCenter ? Math.Min(y, canvas.Height - y) : dy < 0 ? y : canvas.Height - y;
        double width = Math.Min(Math.Abs(dx), availableX), height = Math.Min(Math.Abs(dy), availableY);
        if (square) width = height = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Min(availableX, availableY));
        return fromCenter ? new(x - width, y - height, width * 2, height * 2)
            : new(x + (dx < 0 ? -width : 0), y + (dy < 0 ? -height : 0), width, height);
    }

    public static RectD Move(RectD bounds, double dx, double dy, SizeD canvas) => AnnotationBoundsEditor.Move(bounds, dx, dy, canvas);
}
