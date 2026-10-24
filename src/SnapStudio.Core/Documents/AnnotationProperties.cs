namespace SnapStudio.Core.Documents;

[Flags]
public enum AnnotationProperty
{
    None = 0,
    Color = 1,
    StrokeWidth = 2,
    TextSize = 4,
    Opacity = 8,
    Corners = 16,
    Font = 32,
    TextContent = 64,
    ShapeFill = 128,
    BlurStrength = 256
}

public static class AnnotationProperties
{
    public static AnnotationProperty For(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rectangle => AnnotationProperty.Color | AnnotationProperty.StrokeWidth
            | AnnotationProperty.Opacity | AnnotationProperty.Corners | AnnotationProperty.ShapeFill,
        AnnotationKind.Ellipse => AnnotationProperty.Color | AnnotationProperty.StrokeWidth
            | AnnotationProperty.Opacity | AnnotationProperty.ShapeFill,
        AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Pen =>
            AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity,
        AnnotationKind.Text => AnnotationProperty.Color | AnnotationProperty.TextSize | AnnotationProperty.Opacity
            | AnnotationProperty.Font | AnnotationProperty.TextContent,
        AnnotationKind.Highlight or AnnotationKind.Fill => AnnotationProperty.Color | AnnotationProperty.Opacity,
        AnnotationKind.Image or AnnotationKind.Vector => AnnotationProperty.Opacity,
        AnnotationKind.Blur => AnnotationProperty.BlurStrength,
        _ => AnnotationProperty.None
    };

    public static AnnotationProperty Common(IEnumerable<AnnotationKind> kinds)
    {
        using IEnumerator<AnnotationKind> iterator = kinds.GetEnumerator();
        if (!iterator.MoveNext()) return AnnotationProperty.None;
        AnnotationProperty shared = For(iterator.Current);
        while (iterator.MoveNext()) shared &= For(iterator.Current);
        return shared;
    }
}
