using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class LayerClipping
{
    public static void Validate(AnnotationLayer layer, int width, int height)
    {
        if (layer.Mask is { } mask)
        {
            if (mask.Rectangles is null) throw new InvalidDataException("The layer mask is missing its regions.");
            foreach (RectD rectangle in mask.Rectangles) ValidateBounds(rectangle, width, height);
        }
        if (layer.CropBounds is RectD crop) ValidateBounds(crop, width, height);
    }

    private static void ValidateBounds(RectD bounds, int width, int height)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)
            || bounds.X < 0 || bounds.Y < 0 || bounds.Width < 0 || bounds.Height < 0 || bounds.Right > width + .00001 || bounds.Bottom > height + .00001)
            throw new InvalidDataException("Layer mask and crop regions must be inside the image.");
    }

    public static IReadOnlyList<RectD>? GetRectangles(AnnotationLayer layer, int width, int height)
    {
        if (layer.Mask is not { IsEnabled: true } && layer.CropBounds is null) return null;
        RectD crop = layer.CropBounds ?? new(0, 0, width, height);
        return (layer.Mask is { IsEnabled: true } mask ? mask.Rectangles : [crop])
            .Select(region => Intersect(region, crop)).Where(region => region.Width > 0 && region.Height > 0).ToArray();
    }

    public static RectD Intersect(RectD first, RectD second)
    {
        double x = Math.Max(first.X, second.X), y = Math.Max(first.Y, second.Y);
        return new(x, y, Math.Max(0, Math.Min(first.Right, second.Right) - x), Math.Max(0, Math.Min(first.Bottom, second.Bottom) - y));
    }

    public static AnnotationLayer Clone(AnnotationLayer layer) => new()
    {
        Id = layer.Id, Name = layer.Name, ColorLabel = layer.ColorLabel, CustomColor = layer.CustomColor,
        IsVisible = layer.IsVisible, Mask = layer.Mask?.DeepCopy(), CropBounds = layer.CropBounds
    };

    public static List<AnnotationLayer> Transform(IEnumerable<AnnotationLayer> layers, double offsetX, double offsetY, double scaleX, double scaleY, int width, int height)
    {
        RectD TransformBounds(RectD region)
        {
            double left = Math.Clamp((region.X + offsetX) * scaleX, 0, width), top = Math.Clamp((region.Y + offsetY) * scaleY, 0, height);
            double right = Math.Clamp((region.Right + offsetX) * scaleX, left, width), bottom = Math.Clamp((region.Bottom + offsetY) * scaleY, top, height);
            return new(left, top, right - left, bottom - top);
        }
        return layers.Select(layer =>
        {
            var copy = Clone(layer);
            if (copy.Mask is { } mask) copy.Mask = mask with { Rectangles = mask.Rectangles.Select(TransformBounds).ToArray() };
            if (copy.CropBounds is RectD crop) copy.CropBounds = TransformBounds(crop);
            return copy;
        }).ToList();
    }
}
