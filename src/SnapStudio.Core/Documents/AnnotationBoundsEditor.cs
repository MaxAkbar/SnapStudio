using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public enum AnnotationBoundsHandle
{
    Move,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Start,
    End
}

public static class AnnotationBoundsEditor
{
    public const double MinimumSize = 4;

    public static RectD Move(
        RectD bounds,
        double deltaX,
        double deltaY,
        SizeD sourceSize)
    {
        RectD normalized = Normalize(bounds);
        double minimumDeltaX = -normalized.X;
        double maximumDeltaX = sourceSize.Width - normalized.Right;
        double minimumDeltaY = -normalized.Y;
        double maximumDeltaY = sourceSize.Height - normalized.Bottom;
        double clampedDeltaX = Clamp(deltaX, minimumDeltaX, maximumDeltaX);
        double clampedDeltaY = Clamp(deltaY, minimumDeltaY, maximumDeltaY);

        return new RectD(
            bounds.X + clampedDeltaX,
            bounds.Y + clampedDeltaY,
            bounds.Width,
            bounds.Height);
    }

    public static RectD Resize(
        RectD bounds,
        AnnotationBoundsHandle handle,
        PointD dragPoint,
        SizeD sourceSize,
        double minimumSize = MinimumSize)
    {
        RectD normalized = Normalize(bounds);
        double minimumWidth = Math.Min(Math.Max(1, minimumSize), Math.Max(1, sourceSize.Width));
        double minimumHeight = Math.Min(Math.Max(1, minimumSize), Math.Max(1, sourceSize.Height));
        double left = normalized.X;
        double top = normalized.Y;
        double right = normalized.Right;
        double bottom = normalized.Bottom;
        double x = Clamp(dragPoint.X, 0, sourceSize.Width);
        double y = Clamp(dragPoint.Y, 0, sourceSize.Height);

        if (handle is AnnotationBoundsHandle.Left
            or AnnotationBoundsHandle.TopLeft
            or AnnotationBoundsHandle.BottomLeft)
        {
            left = Clamp(x, 0, Math.Max(0, right - minimumWidth));
        }

        if (handle is AnnotationBoundsHandle.Right
            or AnnotationBoundsHandle.TopRight
            or AnnotationBoundsHandle.BottomRight)
        {
            right = Clamp(x, Math.Min(sourceSize.Width, left + minimumWidth), sourceSize.Width);
        }

        if (handle is AnnotationBoundsHandle.Top
            or AnnotationBoundsHandle.TopLeft
            or AnnotationBoundsHandle.TopRight)
        {
            top = Clamp(y, 0, Math.Max(0, bottom - minimumHeight));
        }

        if (handle is AnnotationBoundsHandle.Bottom
            or AnnotationBoundsHandle.BottomLeft
            or AnnotationBoundsHandle.BottomRight)
        {
            bottom = Clamp(y, Math.Min(sourceSize.Height, top + minimumHeight), sourceSize.Height);
        }

        return new RectD(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static RectD MoveEndpoint(
        RectD bounds,
        AnnotationBoundsHandle handle,
        PointD dragPoint,
        SizeD sourceSize)
    {
        double x = Clamp(dragPoint.X, 0, sourceSize.Width);
        double y = Clamp(dragPoint.Y, 0, sourceSize.Height);

        return handle switch
        {
            AnnotationBoundsHandle.Start => new RectD(
                x,
                y,
                bounds.Right - x,
                bounds.Bottom - y),
            AnnotationBoundsHandle.End => new RectD(
                bounds.X,
                bounds.Y,
                x - bounds.X,
                y - bounds.Y),
            _ => bounds
        };
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (minimum > maximum)
        {
            return minimum;
        }

        return Math.Clamp(value, minimum, maximum);
    }
}
