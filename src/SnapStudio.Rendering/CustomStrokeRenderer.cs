using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Svg;

namespace SnapStudio.Rendering;

public sealed record CustomStrokePreview(AnnotationImage Image, RectD Bounds);

public static class CustomStrokeRenderer
{
    public static CustomStrokePreview? Preview(AnnotationObject annotation, double zoom)
    {
        if (annotation.Style is not { Pattern: StrokePattern.Custom, CustomPattern: not null }) return null;
        var result = CreateSvg(annotation);
        if (result is null) return null;
        return new(SvgRasterizer.Preview(result.Value.Svg, result.Value.Bounds.Width * zoom, result.Value.Bounds.Height * zoom),
            new(result.Value.Bounds.X * zoom, result.Value.Bounds.Y * zoom, result.Value.Bounds.Width * zoom, result.Value.Bounds.Height * zoom));
    }

    public static (string Svg, RectD Bounds)? CreateSvg(AnnotationObject annotation)
    {
        if (annotation.Style is not { Pattern: StrokePattern.Custom, CustomPattern: { } pattern }) return null;
        RectD b = annotation.Bounds;
        var paths = GetPaths(annotation);
        if (paths.Count == 0) return null;
        // Local coordinates make moving objects reuse their cached pattern pixels.
        double left = Math.Min(b.X, b.Right), top = Math.Min(b.Y, b.Bottom);
        var local = paths.Select(c => new StrokeContour(c.Points.Select(p => new PointD(p.X - left, p.Y - top)).ToArray(), c.Closed)).ToArray();
        string? head = null; IReadOnlyList<PointD>? headPoints = null;
        if (annotation.Kind == AnnotationKind.Arrow)
        {
            var outline = ArrowGeometry.CreateOutline(new(b.X, b.Y), new(b.Right, b.Bottom), annotation.Style.StrokeThickness);
            if (outline.Count == 7)
            {
                string N(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);
                string points = string.Join(" ", new[] { outline[2], outline[3], outline[4] }.Select(p => $"{N(p.X - left)},{N(p.Y - top)}"));
                var color = annotation.Style.Stroke;
                headPoints = new[] { outline[2], outline[3], outline[4] }.Select(p => new PointD(p.X - left, p.Y - top)).ToArray();
                head = $"<polygon xmlns=\"http://www.w3.org/2000/svg\" points=\"{points}\" style=\"fill:#{color.R:X2}{color.G:X2}{color.B:X2}!important;stroke:none!important\" fill-opacity=\"{N(color.A / 255d)}\"/>";
            }
        }
        var result = CustomStrokeGeometry.CreateSvg(local, pattern, annotation.Style.StrokeThickness, head, headPoints);
        return result;
    }

    public static void Draw(Graphics graphics, AnnotationObject annotation)
    {
        var result = CreateSvg(annotation); if (result is null) return;
        var b = result.Value.Bounds; var a = annotation.Bounds;
        float x = (float)Math.Min(a.X, a.Right), y = (float)Math.Min(a.Y, a.Bottom);
        var visible = RectangleF.Intersect(new(x + (float)b.X, y + (float)b.Y, (float)b.Width, (float)b.Height), graphics.VisibleClipBounds);
        if (visible.Width <= 0 || visible.Height <= 0) return;
        var root = XElement.Parse(result.Value.Svg);
        root.SetAttributeValue("viewBox", FormattableString.Invariant($"{visible.X - x} {visible.Y - y} {visible.Width} {visible.Height}"));
        SvgRasterizer.Draw(graphics, root.ToString(SaveOptions.DisableFormatting), visible, annotation.Style.Opacity);
    }

    public static string HideSvgStrokes(string svg)
    {
        var root = XElement.Parse(svg);
        foreach (var element in root.DescendantsAndSelf().Where(e => new[] { "path", "line", "rect", "ellipse", "circle", "polyline", "polygon", "text", "use" }.Contains(e.Name.LocalName)))
            element.SetAttributeValue("style", ((string?)element.Attribute("style") ?? "") + ";stroke:none!important");
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static List<StrokeContour> GetPaths(AnnotationObject annotation)
    {
        var paths = new List<StrokeContour>(); RectD b = annotation.Bounds;
        using var path = new GraphicsPath();
        switch (annotation.Kind)
        {
            case AnnotationKind.Line: path.AddLine((float)b.X, (float)b.Y, (float)b.Right, (float)b.Bottom); break;
            case AnnotationKind.Arrow:
                var outline = ArrowGeometry.CreateOutline(new(b.X, b.Y), new(b.Right, b.Bottom), annotation.Style.StrokeThickness);
                if (outline.Count == 7) path.AddLine((float)b.X, (float)b.Y, (float)((outline[2].X + outline[4].X) / 2), (float)((outline[2].Y + outline[4].Y) / 2));
                break;
            case AnnotationKind.Pen: paths.Add(new(FreehandStroke.GetPoints(annotation).ToArray())); return paths;
            case AnnotationKind.Rectangle:
                var rectangle = new RectangleF((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                float r = (float)Math.Min(annotation.Style.CornerRadius, Math.Min(b.Width, b.Height) / 2), d = r * 2;
                if (r <= .01) path.AddRectangle(rectangle);
                else { path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90); path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90); path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90); path.CloseFigure(); }
                break;
            case AnnotationKind.Ellipse: path.AddEllipse((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height); break;
            case AnnotationKind.Vector when annotation.Vector is { } vector:
                var document = SvgDocument.FromSvg<SvgDocument>(SvgShape.Normalize(vector.Svg));
                using (var bitmap = new Bitmap(1, 1)) using (var graphics = Graphics.FromImage(bitmap)) using (var renderer = SvgRenderer.FromGraphics(graphics))
                {
                    IEnumerable<SvgElement> Descendants(SvgElement parent) => parent.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));
                    foreach (var element in Descendants(document).OfType<SvgVisualElement>().Where(e => e is not SvgGroup && e is not SvgSymbol))
                    {
                        var ancestors = new List<SvgElement>(); for (SvgElement? parent = element; parent is not null && parent != document; parent = parent.Parent) ancestors.Add(parent);
                        if (ancestors.Any(e => e is SvgDefinitionList or SvgClipPath or SvgMask or SvgPatternServer or SvgSymbol || e.Display == "none" || e.Visibility == "hidden")) continue;
                        var original = element.Path(renderer); if (original is null || original.PointCount == 0) continue;
                        using var shape = (GraphicsPath)original.Clone();
                        foreach (var ancestor in ancestors) if (ancestor.Transforms is { Count: > 0 }) { using var transform = ancestor.Transforms.GetMatrix(); shape.Transform(transform); }
                        var v = document.ViewBox;
                        using var placement = new Matrix((float)(b.Width / v.Width), 0, 0, (float)(b.Height / v.Height), (float)(b.X - v.MinX * b.Width / v.Width), (float)(b.Y - v.MinY * b.Height / v.Height));
                        shape.Transform(placement); Flatten(shape, paths);
                    }
                }
                return paths;
        }
        Flatten(path, paths); return paths;
    }

    internal static IReadOnlyList<StrokeContour> GetContours(GraphicsPath path)
    {
        var result = new List<StrokeContour>(); Flatten(path, result); return result;
    }

    private static void Flatten(GraphicsPath path, List<StrokeContour> paths)
    {
        if (path.PointCount == 0) return;
        path.Flatten(null, .2f); var points = path.PathPoints; var types = path.PathTypes; var current = new List<PointD>();
        for (int i = 0; i < points.Length; i++)
        {
            if ((types[i] & 7) == 0 && current.Count > 0) { paths.Add(new(current.ToArray())); current.Clear(); }
            current.Add(new(points[i].X, points[i].Y));
            if ((types[i] & 128) != 0) { paths.Add(new(current.ToArray(), true)); current.Clear(); }
        }
        if (current.Count > 0) paths.Add(new(current));
    }
}
