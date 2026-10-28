using System.Globalization;
using System.Xml.Linq;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public sealed record CustomLinePattern(string Svg, string Name, double Scale = 1, double Spacing = 1)
{
    public static CustomLinePattern Normalize(CustomLinePattern pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern.Name) || pattern.Name.Trim().Length > 80
            || !double.IsFinite(pattern.Scale) || pattern.Scale is < .1 or > 10
            || !double.IsFinite(pattern.Spacing) || pattern.Spacing is < .1 or > 10)
            throw new InvalidDataException("A line pattern needs a name, and scale and spacing between 10% and 1000%.");
        string svg = SvgShape.Normalize(pattern.Svg);
        var document = XDocument.Parse(svg);
        if (svg.Length > 64_000 || document.Descendants().Count() > 512 || document.Descendants().Any(e => e.Ancestors().Count() > 24))
            throw new InvalidDataException("Use a repeating motif with at most 512 elements and 64 KB of SVG.");
        return pattern with { Svg = svg, Name = pattern.Name.Trim() };
    }

    public static void ValidateStyle(AnnotationStyle style)
    {
        if (style.Pattern == StrokePattern.Custom && style.CustomPattern is null)
            throw new InvalidDataException("Choose an SVG motif for the custom line.");
        if (style.CustomPattern is { } pattern)
        {
            pattern = Normalize(pattern);
            double[] view = XElement.Parse(pattern.Svg).Attribute("viewBox")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            double height = style.StrokeThickness * 4 * pattern.Scale;
            if (!double.IsFinite(height) || height <= 0 || Math.Max(height, height * view[2] / view[3]) > 4096)
                throw new InvalidDataException("Keep SVG repeats within 4096 pixels by reducing line width or pattern scale.");
        }
    }
}

public sealed record StrokeContour(IReadOnlyList<PointD> Points, bool Closed = false);
public sealed record PatternPlacement(PointD Center, double Angle);

public static class CustomStrokeGeometry
{
    public const int MaximumRepeats = 2048;
    public const string StarterSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 16\"><path d=\"M0 8 L8 2 L16 8 L24 14 L32 8\" fill=\"none\" stroke=\"#4CC2FF\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>";
    public static IReadOnlyList<PatternPlacement> Place(IEnumerable<StrokeContour> contours, double pitch)
    {
        if (!double.IsFinite(pitch) || pitch <= 0) throw new ArgumentOutOfRangeException(nameof(pitch));
        var paths = contours.Select(c => new StrokeContour(c.Closed && c.Points.Count > 1 && c.Points[0] != c.Points[^1]
            ? c.Points.Append(c.Points[0]).ToArray() : c.Points, c.Closed)).ToArray();
        if (paths.Any(c => c.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))) throw new InvalidDataException("Stroke paths need finite points.");
        double Length(StrokeContour c) => c.Points.Zip(c.Points.Skip(1), (a, b) => double.Hypot(b.X - a.X, b.Y - a.Y)).Sum();
        pitch = Math.Max(pitch, paths.Sum(Length) / Math.Max(1, MaximumRepeats - paths.Length));
        var result = new List<PatternPlacement>();
        foreach (var path in paths)
        {
            if (result.Count >= MaximumRepeats) break;
            if (path.Points.Count == 0) continue;
            double total = Length(path), next = 0, walked = 0;
            if (total < .000001) { result.Add(new(path.Points[0], 0)); continue; }
            for (int i = 1; i < path.Points.Count && result.Count < MaximumRepeats; i++)
            {
                PointD a = path.Points[i - 1], b = path.Points[i]; double length = double.Hypot(b.X - a.X, b.Y - a.Y);
                if (length < .000001) continue;
                while (next <= walked + length + .000001 && (!path.Closed || next < total - .000001) && result.Count < MaximumRepeats)
                {
                    double t = Math.Clamp((next - walked) / length, 0, 1);
                    result.Add(new(new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI)); next += pitch;
                }
                walked += length;
            }
        }
        return result;
    }

    public static (string Svg, RectD Bounds) CreateSvg(IEnumerable<StrokeContour> contours, CustomLinePattern pattern, double strokeWidth, string? extra = null, IReadOnlyList<PointD>? extraPoints = null)
    {
        pattern = CustomLinePattern.Normalize(pattern);
        var paths = contours.ToArray(); var points = paths.SelectMany(c => c.Points).Concat(extraPoints ?? []).ToArray();
        if (points.Length == 0) throw new InvalidDataException("This object has no stroke path.");
        var motif = XElement.Parse(pattern.Svg); XNamespace ns = "http://www.w3.org/2000/svg";
        double[] view = motif.Attribute("viewBox")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        double height = Math.Max(1, strokeWidth) * 4 * pattern.Scale, width = height * view[2] / view[3];
        double pad = double.Hypot(width, height) / 2 + 1;
        var bounds = new RectD(points.Min(p => p.X) - pad, points.Min(p => p.Y) - pad,
            points.Max(p => p.X) - points.Min(p => p.X) + pad * 2, points.Max(p => p.Y) - points.Min(p => p.Y) + pad * 2);
        string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        string id = "snap-pattern-tile"; var ids = motif.DescendantsAndSelf().Select(e => (string?)e.Attribute("id")).ToHashSet(); while (ids.Contains(id)) id += "-tile";
        motif.SetAttributeValue("x", N(-width / 2)); motif.SetAttributeValue("y", N(-height / 2)); motif.SetAttributeValue("width", N(width)); motif.SetAttributeValue("height", N(height));
        var root = new XElement(ns + "svg", new XAttribute("viewBox", $"{N(bounds.X)} {N(bounds.Y)} {N(bounds.Width)} {N(bounds.Height)}"), new XAttribute("preserveAspectRatio", "none"),
            new XElement(ns + "defs", new XElement(ns + "g", new XAttribute("id", id), motif)));
        foreach (var item in Place(paths, Math.Max(.25, width * pattern.Spacing)))
            root.Add(new XElement(ns + "use", new XAttribute("href", "#" + id), new XAttribute("transform", $"translate({N(item.Center.X)} {N(item.Center.Y)}) rotate({N(item.Angle)})")));
        if (extra is not null) root.Add(XElement.Parse(extra));
        return (SvgShape.Normalize(root.ToString(SaveOptions.DisableFormatting)), bounds);
    }
}
