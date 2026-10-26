using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Documents;

/// <summary>Retains the drawing intent of a generated perspective path when its object moves.</summary>
public static class PerspectiveObjectBinding
{
    private sealed record Binding(string Tool, PointD Start, PointD End, PerspectiveLineFamily? Family, PerspectiveRectanglePlane Plane);
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public PointD Apply(PointD p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);
    }
    private readonly record struct LegacyKey(string Svg, string Name, RectD View, PerspectiveGridSettings Settings);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<LegacyKey, Binding?> LegacyCache = [];
    private static readonly Queue<LegacyKey> CacheOrder = [];
    private static readonly Regex Number = new(@"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant);
    private static readonly Regex PathTokens = new(@"[MLZ]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant);

    public static string Attach(string svg, string tool, PointD start, PointD end, PerspectiveLineFamily? family = null,
        PerspectiveRectanglePlane plane = PerspectiveRectanglePlane.Side)
    {
        var document = Parse(svg);
        var path = document.Root!.Elements().Single(e => e.Name.LocalName == "path");
        Write(path, new(tool, start, end, family, plane));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    // A cheap eligibility check; legacy paths are fitted against the active grid by TryReproject.
    public static bool IsPerspectiveObject(AnnotationObject annotation)
    {
        if (annotation.Kind != AnnotationKind.Vector || annotation.Vector is null) return false;
        try
        {
            if (!ReadDocument(annotation.Vector.Svg, out _, out var path, out _, out _)) return false;
            if (path.Attribute("data-perspective-tool") is not null) return ReadMetadata(path) is not null;
            return LegacyTool(annotation.Vector.Name, path) is not null && TryPoints(path, out _, out _);
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException or ArgumentException) { return false; }
    }

    /// <summary>
    /// False means an ordinary or manually edited SVG. True with null means a bound perspective object
    /// cannot occupy the requested position; callers should keep its original geometry until a valid move.
    /// </summary>
    public static bool TryReproject(AnnotationObject original, RectD proposedBounds, RectD view, PerspectiveGridSettings settings,
        double scaleX, double scaleY, out AnnotationObject? projected)
    {
        projected = null;
        if (original.Kind != AnnotationKind.Vector || original.Vector is null) return false;
        try
        {
            if (!ReadDocument(original.Vector.Svg, out var document, out var path, out var box, out var matrix)) return false;
            Binding? binding;
            if (path.Attribute("data-perspective-tool") is not null)
            {
                binding = ReadMetadata(path);
                if (binding is null) return false; // A stale signature is a manual path edit, never a legacy candidate.
            }
            else
            {
                string? tool = LegacyTool(original.Vector.Name, path);
                if (tool is null || !TryPoints(path, out var points, out bool closed) || (tool == "rectangle") != closed) return false;
                var key = new LegacyKey(original.Vector.Svg, original.Vector.Name, view, settings.Normalize());
                lock (CacheLock)
                {
                    if (!LegacyCache.TryGetValue(key, out binding))
                    {
                        binding = Infer(tool, points, view, settings);
                        while (CacheOrder.Count >= 64) LegacyCache.Remove(CacheOrder.Dequeue());
                        CacheOrder.Enqueue(key); LegacyCache[key] = binding;
                    }
                }
                if (binding is null) return false;
            }
            if (!FiniteBounds(original.Bounds) || !FiniteBounds(proposedBounds) || !FiniteBounds(view)
                || !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0) return true;
            PointD Map(PointD local)
            {
                PointD p = matrix.Apply(local);
                return new(proposedBounds.X + (p.X - box.X) / box.Width * proposedBounds.Width,
                    proposedBounds.Y + (p.Y - box.Y) / box.Height * proposedBounds.Height);
            }
            PointD start = Map(binding.Start), end = Map(binding.End);
            if (!Finite(start) || !Finite(end)) return true;
            var normalized = settings.Normalize();
            bool curvedFamily = binding.Family == PerspectiveLineFamily.CurvedVertical
                && normalized.Mode is CanvasGridMode.FourPoint or CanvasGridMode.FivePoint
                || binding.Family == PerspectiveLineFamily.CurvedHorizontal && normalized.Mode == CanvasGridMode.FivePoint;
            if (binding.Tool == "line" && curvedFamily)
            {
                double cx = view.X + view.Width * normalized.CenterX / 100, cy = view.Y + view.Height * normalized.Horizon / 100;
                double radius = Math.Min(view.Width, view.Height) * normalized.Radius / 100;
                if (Math.Pow((start.X - cx) / radius, 2) + Math.Pow((start.Y - cy) / radius, 2) > 1 + 1e-8) return true;
            }
            AnnotationStyle paintBounds = original.Style with { StrokeThickness = StrokeWidth(document.Root!, path, original.Style.StrokeThickness) };
            AnnotationObject? generated;
            if (binding.Tool == "rectangle")
            {
                var shape = PerspectiveRectangle.Create(start, end, view, settings, binding.Plane);
                if (shape is null || shape.Bounds.Width < 1e-6 || shape.Bounds.Height < 1e-6) return true;
                generated = shape.ToAnnotation(paintBounds);
            }
            else
            {
                var line = PerspectiveCurveDrawing.Draw(start, end, view, settings, scaleX, scaleY, binding.Family);
                if (line.Points.Count < 2 || Length(line.Points) < 1e-6) return true;
                generated = line.ToAnnotation(paintBounds);
            }
            // Preserve the existing SVG paints and definitions, including colors edited in the SVG designer.
            var replacement = Parse(generated.Vector!.Svg);
            var nextPath = replacement.Root!.Elements().Single(e => e.Name.LocalName == "path");
            if (binding.Tool == "line" && binding.Family is {} family)
                nextPath.SetAttributeValue("data-perspective-family", (int)family);
            foreach (var attribute in path.Attributes().Where(a => a.Name.LocalName.StartsWith("data-perspective-", StringComparison.Ordinal)).ToArray()) attribute.Remove();
            path.SetAttributeValue("d", (string?)nextPath.Attribute("d"));
            path.Attribute("transform")?.Remove();
            foreach (var attribute in nextPath.Attributes().Where(a => a.Name.LocalName.StartsWith("data-perspective-", StringComparison.Ordinal)))
                path.SetAttributeValue(attribute.Name, attribute.Value);
            document.Root!.SetAttributeValue("viewBox", (string?)replacement.Root.Attribute("viewBox"));
            document.Root.SetAttributeValue("preserveAspectRatio", "none");
            if (document.Root.Attribute("width") is not null) document.Root.SetAttributeValue("width", N(generated.Bounds.Width));
            if (document.Root.Attribute("height") is not null) document.Root.SetAttributeValue("height", N(generated.Bounds.Height));
            projected = new()
            {
                Id = original.Id, Kind = original.Kind, LayerId = original.LayerId, IsVisible = original.IsVisible,
                Bounds = generated.Bounds, Text = original.Text, Style = original.Style, Image = original.Image,
                StrokePoints = [.. original.StrokePoints], FillRectangles = [.. original.FillRectangles],
                Vector = original.Vector with { Svg = document.ToString(SaveOptions.DisableFormatting) }
            };
            return true;
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { projected = null; return false; }
    }

    private static Binding? ReadMetadata(XElement path)
    {
        string? tool = (string?)path.Attribute("data-perspective-tool");
        if (tool is not ("line" or "rectangle") || (string?)path.Attribute("data-perspective-path") != (string?)path.Attribute("d")
            || !Point((string?)path.Attribute("data-perspective-start"), out var start)
            || !Point((string?)path.Attribute("data-perspective-end"), out var end)) return null;
        int family = -1, plane = (int)PerspectiveRectanglePlane.Side;
        if (tool == "line" && (!int.TryParse((string?)path.Attribute("data-perspective-family"), NumberStyles.Integer, CultureInfo.InvariantCulture, out family)
            || family < -1 || family > (int)PerspectiveLineFamily.CenterVanishingPoint)) return null;
        if (tool == "rectangle" && (!int.TryParse((string?)path.Attribute("data-perspective-plane"), NumberStyles.Integer, CultureInfo.InvariantCulture, out plane)
            || !Enum.IsDefined((PerspectiveRectanglePlane)plane))) return null;
        if (!TryPoints(path, out _, out bool closed) || (tool == "rectangle") != closed) return null;
        return new(tool, start, end, family == -1 ? null : (PerspectiveLineFamily)family, (PerspectiveRectanglePlane)plane);
    }

    private static void Write(XElement path, Binding binding)
    {
        path.SetAttributeValue("data-perspective-tool", binding.Tool);
        path.SetAttributeValue("data-perspective-start", N(binding.Start.X) + "," + N(binding.Start.Y));
        path.SetAttributeValue("data-perspective-end", N(binding.End.X) + "," + N(binding.End.Y));
        path.SetAttributeValue("data-perspective-family", binding.Family is {} family ? (int)family : -1);
        path.SetAttributeValue("data-perspective-plane", (int)binding.Plane);
        path.SetAttributeValue("data-perspective-path", (string?)path.Attribute("d"));
    }

    private static bool ReadDocument(string svg, out XDocument document, out XElement path, out RectD box, out Matrix matrix)
    {
        document = Parse(svg); path = null!; box = default; matrix = new(1, 0, 0, 1, 0, 0);
        var root = document.Root;
        if (root?.Name.LocalName != "svg" || root.Attribute("transform") is not null) return false;
        var drawings = root.Elements().Where(e => e.Name.LocalName is not ("defs" or "style" or "title" or "desc" or "metadata")).ToArray();
        if (drawings.Length != 1 || drawings[0].Name.LocalName != "path") return false;
        path = drawings[0];
        // SVG CSS transforms and CSS path geometry are outside this single-path binding model.
        // Paint declarations remain untouched; custom geometry must keep its ordinary SVG behavior.
        var css = root.Descendants().Where(e => e.Name.LocalName == "style").Select(e => e.Value)
            .Concat(new[] { (string?)root.Attribute("style") ?? "", (string?)path.Attribute("style") ?? "" });
        if (css.Any(value => Regex.IsMatch(CssWithoutComments(value), @"(?:^|[;{])\s*(?:transform(?:-[a-z]+)?|translate|rotate|scale|d|x|y)\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))) return false;
        if (!Numbers((string?)root.Attribute("viewBox"), 4, out var values)) return false;
        box = new(values[0], values[1], values[2], values[3]); if (!FiniteBounds(box)) return false;
        if ((string?)path.Attribute("transform") is { Length: > 0 } transform)
        {
            var match = Regex.Match(transform, @"^\s*matrix\s*\(([^)]*)\)\s*$", RegexOptions.CultureInvariant);
            if (!match.Success || !Numbers(match.Groups[1].Value, 6, out var m)) return false;
            matrix = new(m[0], m[1], m[2], m[3], m[4], m[5]);
            if (Math.Abs(m[0] * m[3] - m[1] * m[2]) < 1e-12) return false;
        }
        return true;
    }

    private static XDocument Parse(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg) || svg.Length > SvgShape.MaximumCharacters) throw new InvalidDataException("Unsupported perspective SVG.");
        using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = SvgShape.MaximumCharacters });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string? LegacyTool(string name, XElement path) => name == "Perspective line" || (string?)path.Attribute("data-name") == "Perspective line" ? "line"
        : name == "Perspective rectangle" || (string?)path.Attribute("data-name") == "Perspective rectangle" ? "rectangle" : null;

    private static Binding? Infer(string tool, IReadOnlyList<PointD> points, RectD view, PerspectiveGridSettings settings)
    {
        double tolerance = Math.Max(.005, Math.Max(view.Width, view.Height) * .00002);
        if (tool == "line")
        {
            Binding? best = null; double score = double.PositiveInfinity;
            foreach (var family in Enum.GetValues<PerspectiveLineFamily>())
            {
                var candidate = PerspectiveCurveDrawing.Draw(points[0], points[^1], view, settings, locked: family);
                if (candidate.Direction != family) continue;
                double error = Compare(points, candidate.Points, tolerance);
                if (error < score) { score = error; best = new("line", points[0], points[^1], family, PerspectiveRectanglePlane.Side); }
            }
            return score <= tolerance ? best : null;
        }
        var outline = points.Concat(new[] { points[0] }).ToArray();
        var indices = Enumerable.Range(1, points.Count - 1).OrderByDescending(i => Turn(points, i)).ToArray();
        Binding? result = null; double resultScore = double.PositiveInfinity;
        foreach (int i in indices)
        foreach (var plane in Enum.GetValues<PerspectiveRectanglePlane>())
        {
            var candidate = PerspectiveRectangle.Create(points[0], points[i], view, settings, plane);
            if (candidate is null) continue;
            double error = Compare(outline, candidate.Outline, tolerance);
            if (error < resultScore) { resultScore = error; result = new("rectangle", points[0], points[i], null, plane); }
            if (error <= tolerance * .1) return result;
        }
        return resultScore <= tolerance ? result : null;
    }

    private static double Turn(IReadOnlyList<PointD> points, int i)
    {
        var a = points[(i + points.Count - 1) % points.Count]; var b = points[i]; var c = points[(i + 1) % points.Count];
        return Math.Abs(Math.Atan2((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X), (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y)));
    }

    private static double Compare(IReadOnlyList<PointD> a, IReadOnlyList<PointD> b, double tolerance)
    {
        if (a.Count < 2 || b.Count < 2 || Distance(a[0], b[0]) > tolerance || Distance(a[^1], b[^1]) > tolerance) return double.PositiveInfinity;
        double la = Length(a), lb = Length(b); if (Math.Abs(la - lb) > Math.Max(tolerance * 2, la * .001)) return double.PositiveInfinity;
        double Directed(IReadOnlyList<PointD> from, IReadOnlyList<PointD> to)
        {
            double maximum = 0;
            foreach (var p in from)
            {
                double nearest = double.PositiveInfinity;
                for (int i = 1; i < to.Count; i++)
                {
                    PointD x = to[i - 1], y = to[i]; double dx = y.X - x.X, dy = y.Y - x.Y;
                    double length = dx * dx + dy * dy, t = length < 1e-20 ? 0 : Math.Clamp(((p.X - x.X) * dx + (p.Y - x.Y) * dy) / length, 0, 1);
                    nearest = Math.Min(nearest, Distance(p, new(x.X + t * dx, x.Y + t * dy)));
                }
                maximum = Math.Max(maximum, nearest); if (maximum > tolerance) return maximum;
            }
            return maximum;
        }
        return Math.Max(Directed(a, b), Directed(b, a));
    }

    private static bool TryPoints(XElement path, out PointD[] points, out bool closed)
    {
        points = []; closed = false; string d = (string?)path.Attribute("d") ?? "";
        if (d.Length > 100_000 || !string.IsNullOrWhiteSpace(PathTokens.Replace(d, "").Replace(",", ""))) return false;
        var tokens = PathTokens.Matches(d).Select(m => m.Value).ToArray();
        var result = new List<PointD>(); int offset = 0;
        while (offset < tokens.Length)
        {
            string command = tokens[offset++];
            if (command == "Z") { if (offset != tokens.Length || result.Count < 3) return false; closed = true; break; }
            if (command != (result.Count == 0 ? "M" : "L") || offset + 1 >= tokens.Length
                || !Value(tokens[offset++], out double x) || !Value(tokens[offset++], out double y)) return false;
            result.Add(new(x, y)); if (result.Count > 512) return false;
        }
        if (closed && result.Count > 1 && Distance(result[0], result[^1]) < 1e-7) result.RemoveAt(result.Count - 1);
        points = result.ToArray(); return points.Length >= (closed ? 3 : 2);
    }

    private static bool Point(string? text, out PointD point)
    {
        point = default; if (!Numbers(text, 2, out var values)) return false; point = new(values[0], values[1]); return true;
    }
    private static bool Numbers(string? text, int count, out double[] values)
    {
        values = []; if (text is null || !string.IsNullOrWhiteSpace(Number.Replace(text, "").Replace(",", ""))) return false;
        var matches = Number.Matches(text); if (matches.Count != count) return false;
        var parsed = new double[count]; for (int i = 0; i < count; i++) if (!Value(matches[i].Value, out parsed[i])) return false;
        values = parsed; return true;
    }
    private static double StrokeWidth(XElement root, XElement path, double fallback)
    {
        double value = fallback;
        foreach (var element in new[] { root, path })
        {
            if (Value((string?)element.Attribute("stroke-width"), out double attribute)) value = attribute;
            var inline = Regex.Match((string?)element.Attribute("style") ?? "", @"(?:^|;)\s*stroke-width\s*:\s*([^;]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (inline.Success && Value(inline.Groups[1].Value.Trim(), out double style)) value = style;
        }
        return Math.Clamp(double.IsFinite(value) ? value : fallback, 0, 10000);
    }
    private static string CssWithoutComments(string css)
    {
        var result = new global::System.Text.StringBuilder(css.Length);
        int offset = 0;
        while (offset < css.Length)
        {
            int start = css.IndexOf("/*", offset, StringComparison.Ordinal);
            if (start < 0) { result.Append(css, offset, css.Length - offset); break; }
            result.Append(css, offset, start - offset);
            int end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0) break; // An unterminated CSS comment consumes the rest of the stylesheet.
            offset = end + 2;
        }
        return result.ToString();
    }
    private static bool Value(string? text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static bool Finite(PointD p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
    private static bool FiniteBounds(RectD b) => new[] { b.X, b.Y, b.Width, b.Height, b.Right, b.Bottom }.All(double.IsFinite) && b.Width > 0 && b.Height > 0;
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static double Distance(PointD a, PointD b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static double Length(IReadOnlyList<PointD> points) => Enumerable.Range(1, points.Count - 1).Sum(i => Distance(points[i - 1], points[i]));
}
