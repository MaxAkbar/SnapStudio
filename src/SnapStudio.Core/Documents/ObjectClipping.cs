using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class ObjectClipping
{
    public static IReadOnlyList<RectD>? Combine(IReadOnlyList<RectD>? first, IReadOnlyList<RectD>? second) => first is null ? second : second is null ? first
        : first.SelectMany(a => second.Select(b => LayerClipping.Intersect(a, b))).Where(r => r.Width > 0 && r.Height > 0).ToArray();

    public static IReadOnlyList<RectD>? GetRectangles(AnnotationObject annotation)
    {
        if (annotation.ClipRegions is null) return null;
        var b = AnnotationBoundsEditor.CombinedBounds([annotation.Bounds]);
        return annotation.ClipRegions.Select(r => new RectD(b.X + r.X * Math.Max(1, b.Width), b.Y + r.Y * Math.Max(1, b.Height),
            r.Width * Math.Max(1, b.Width), r.Height * Math.Max(1, b.Height))).ToArray();
    }

    public static void RetainLayerClip(CaptureDocument document, AnnotationObject annotation, AnnotationLayer layer)
    {
        var clip = Combine(GetRectangles(annotation), LayerClipping.GetRectangles(layer, document.SourceImage.Width, document.SourceImage.Height));
        if (clip is null) return;
        var b = AnnotationBoundsEditor.CombinedBounds([annotation.Bounds]);
        annotation.ClipRegions = clip.Select(r => new RectD((r.X - b.X) / Math.Max(1, b.Width), (r.Y - b.Y) / Math.Max(1, b.Height),
            r.Width / Math.Max(1, b.Width), r.Height / Math.Max(1, b.Height))).ToList();
    }

    public static void Validate(AnnotationObject annotation)
    {
        if (annotation.ClipRegions is { Count: > 100_000 }) throw new InvalidDataException("The object has too many retained clip regions.");
        foreach (var r in annotation.ClipRegions ?? [])
            if (!new[] { r.X, r.Y, r.Width, r.Height, r.Right, r.Bottom }.All(double.IsFinite) || r.Width < 0 || r.Height < 0)
                throw new InvalidDataException("The object's retained clip is invalid.");
    }
}
