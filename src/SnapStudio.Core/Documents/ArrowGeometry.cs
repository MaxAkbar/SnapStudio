using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class ArrowGeometry
{
    public static IReadOnlyList<PointD> CreateOutline(
        PointD start,
        PointD end,
        double strokeThickness,
        double scale = 1)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!double.IsFinite(length) || length <= 0
            || !double.IsFinite(strokeThickness) || strokeThickness <= 0
            || !double.IsFinite(scale) || scale <= 0)
        {
            return [];
        }

        double unitX = deltaX / length;
        double unitY = deltaY / length;
        double nominalHeadLength = Math.Max(10 * scale, strokeThickness * 4);
        double headLength = Math.Min(nominalHeadLength, length * 0.6);
        double headWidth = Math.Max(8 * scale, strokeThickness * 3) * headLength / nominalHeadLength;
        double halfShaft = Math.Min(strokeThickness, headWidth * 0.6) / 2;
        double neck = length - headLength;

        PointD At(double distance, double offset) => new(
            start.X + unitX * distance - unitY * offset,
            start.Y + unitY * distance + unitX * offset);

        // Fill a single outline so the shaft cannot protrude through the tip or
        // darken the head by applying a translucent color twice.
        return [At(0, halfShaft), At(neck, halfShaft), At(neck, headWidth / 2),
            end, At(neck, -headWidth / 2), At(neck, -halfShaft), At(0, -halfShaft)];
    }
}
