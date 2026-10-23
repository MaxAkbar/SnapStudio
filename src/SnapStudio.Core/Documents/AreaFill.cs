using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class AreaFill
{
    public static IEnumerable<RectD> GetRectangles(AnnotationObject annotation) => annotation.FillRectangles
        .Select(rectangle => new RectD(annotation.Bounds.X + rectangle.X * annotation.Bounds.Width,
            annotation.Bounds.Y + rectangle.Y * annotation.Bounds.Height,
            rectangle.Width * annotation.Bounds.Width, rectangle.Height * annotation.Bounds.Height));

    public static void Validate(AnnotationObject annotation)
    {
        if (!IsFinite(annotation.Bounds) || annotation.Bounds.Width <= 0 || annotation.Bounds.Height <= 0
            || annotation.FillRectangles is null || annotation.FillRectangles.Count == 0
            || annotation.FillRectangles.Any(rectangle => !IsFinite(rectangle) || rectangle.Width <= 0 || rectangle.Height <= 0
                || rectangle.X < 0 || rectangle.Y < 0 || rectangle.Right > 1 + 1e-9 || rectangle.Bottom > 1 + 1e-9))
        {
            throw new InvalidDataException("The fill contains invalid regions or bounds.");
        }
    }

    private static bool IsFinite(RectD rectangle) => double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y)
        && double.IsFinite(rectangle.Width) && double.IsFinite(rectangle.Height);
}
