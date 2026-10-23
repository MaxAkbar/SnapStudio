using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

/// <summary>Finds the frontmost editable shape whose interior contains a bucket click.</summary>
public static class ShapeFill
{
    public static AnnotationObject? FindTarget(CaptureDocument document, PointD point)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return null;
        }

        return DocumentLayers.InPaintOrder(document).Reverse().FirstOrDefault(annotation =>
            annotation.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse
            && DocumentLayers.IsEffectivelyVisible(document, annotation)
            && ContainsInterior(annotation, point));
    }

    private static bool ContainsInterior(AnnotationObject shape, PointD point)
    {
        RectD bounds = shape.Bounds;
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)
            || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
            || bounds.Width <= 0 || bounds.Height <= 0
            || point.X < bounds.X || point.X > bounds.Right
            || point.Y < bounds.Y || point.Y > bounds.Bottom)
        {
            return false;
        }

        if (shape.Kind == AnnotationKind.Ellipse)
        {
            double x = (point.X - bounds.X) / (bounds.Width / 2) - 1;
            double y = (point.Y - bounds.Y) / (bounds.Height / 2) - 1;
            return x * x + y * y <= 1;
        }

        double radius = Math.Min(Math.Max(0, shape.Style.CornerRadius), Math.Min(bounds.Width, bounds.Height) / 2);
        if (radius <= 0.01)
        {
            return true;
        }

        double cornerX = Math.Clamp(point.X, bounds.X + radius, bounds.Right - radius);
        double cornerY = Math.Clamp(point.Y, bounds.Y + radius, bounds.Bottom - radius);
        double dx = (point.X - cornerX) / radius;
        double dy = (point.Y - cornerY) / radius;
        return dx * dx + dy * dy <= 1;
    }
}
