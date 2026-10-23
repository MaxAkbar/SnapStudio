using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class FreehandStroke
{
    public static AnnotationObject Create(IReadOnlyList<PointD> points, AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(style);
        if (points.Count == 0 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new ArgumentException("A pen stroke needs at least one finite point.", nameof(points));
        }

        double left = points.Min(point => point.X);
        double top = points.Min(point => point.Y);
        double width = Math.Max(1, points.Max(point => point.X) - left);
        double height = Math.Max(1, points.Max(point => point.Y) - top);
        return new AnnotationObject
        {
            Kind = AnnotationKind.Pen,
            Bounds = new RectD(left, top, width, height),
            StrokePoints = points.Select(point => new PointD((point.X - left) / width, (point.Y - top) / height)).ToList(),
            Style = style
        };
    }

    public static IEnumerable<PointD> GetPoints(AnnotationObject annotation) => annotation.StrokePoints
        .Select(point => new PointD(annotation.Bounds.X + point.X * annotation.Bounds.Width,
            annotation.Bounds.Y + point.Y * annotation.Bounds.Height));

    public static void Validate(AnnotationObject annotation)
    {
        if (annotation.StrokePoints is null || annotation.StrokePoints.Count == 0
            || !double.IsFinite(annotation.Bounds.X) || !double.IsFinite(annotation.Bounds.Y)
            || !double.IsFinite(annotation.Bounds.Width) || !double.IsFinite(annotation.Bounds.Height)
            || annotation.Bounds.Width <= 0 || annotation.Bounds.Height <= 0
            || annotation.StrokePoints.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)
                || point.X < 0 || point.X > 1 || point.Y < 0 || point.Y > 1))
        {
            throw new InvalidDataException("The pen stroke contains invalid points or bounds.");
        }
    }
}
