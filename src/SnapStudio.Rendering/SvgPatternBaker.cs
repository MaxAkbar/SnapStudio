using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using Svg;

namespace SnapStudio.Rendering;

// SVG exports already contain visible repeats. Rebuild them when a native perspective move
// changes the editable source path, preserving its motif and guide binding together.
internal static class SvgPatternBaker
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Bake(string svg)
    {
        if (!svg.Contains("data-snap-stroke", StringComparison.Ordinal)) return svg;
        XElement root = XElement.Parse(svg); XNamespace ns = root.Name.Namespace;
        var owners = root.Descendants().Where(e => e.Attribute("data-snap-stroke") is not null
            && !e.Ancestors().Any(a => a.Name.LocalName is "defs" or "clipPath" or "mask" or "pattern" or "symbol" || a.Attribute("data-snap-stroke-derived") is not null)).ToArray();
        bool changed = false; int serial = 0;
        foreach (var owner in owners)
        {
            string encoded = owner.Attribute("data-snap-stroke")!.Value;
            CustomLinePattern pattern;
            try { pattern = CustomLinePattern.Normalize(JsonSerializer.Deserialize<CustomLinePattern>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)), Options) ?? throw new InvalidDataException("The SVG line pattern is missing.")); }
            catch (Exception error) when (error is JsonException or FormatException) { throw new InvalidDataException("The SVG contains invalid custom line data.", error); }
            if (owner.Attribute("data-snap-stroke-shape") is not { } signature) continue;
            using JsonDocument shape = JsonDocument.Parse(signature.Value);
            if (!shape.RootElement.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The SVG line geometry data is invalid.");
            bool geometryChanged = attributes.EnumerateObject().Any(a => (string?)owner.Attribute(a.Name) != (a.Value.ValueKind == JsonValueKind.Null ? null : a.Value.GetString()));
            if (!geometryChanged) continue;
            double width = shape.RootElement.GetProperty("width").GetDouble();
            if (!double.IsFinite(width) || width <= 0) throw new InvalidDataException("The SVG line width is invalid.");
            string? originalId = (string?)owner.Attribute("id"); string id = originalId ?? "snap-bake-owner-" + serial++;
            while (originalId is null && root.DescendantsAndSelf().Any(e => (string?)e.Attribute("id") == id)) id += "x";
            owner.SetAttributeValue("id", id);
            var document = SvgDocument.FromSvg<SvgDocument>(root.ToString(SaveOptions.DisableFormatting));
            var visual = document.GetElementById<SvgVisualElement>(id) ?? throw new InvalidDataException("The custom stroke path is missing.");
            using var bitmap = new Bitmap(1, 1); using var graphics = Graphics.FromImage(bitmap); using var renderer = SvgRenderer.FromGraphics(graphics);
            using var path = (GraphicsPath)visual.Path(renderer).Clone();
            var contours = CustomStrokeRenderer.GetContours(path);
            if (contours.Count == 0) continue;
            var baked = CustomStrokeGeometry.CreateSvg(contours, pattern, width);
            var generated = XElement.Parse(baked.Svg);
            string key = owner.Attribute("data-snap-stroke-id")?.Value ?? id;
            var previous = owner.Parent!.Elements().FirstOrDefault(e => (string?)e.Attribute("data-snap-stroke-derived") == key);
            var derived = new XElement(ns + "g", previous?.Attributes() ?? [new XAttribute("data-snap-stroke-derived", key)]);
            derived.Add(generated.Elements());
            string prefix = "snap-native-" + Regex.Replace(key, "[^A-Za-z0-9_-]", "") + "-";
            var existingIds = root.DescendantsAndSelf().Where(e => previous is null || !e.AncestorsAndSelf().Contains(previous)).Select(e => (string?)e.Attribute("id")).ToHashSet();
            while (derived.Descendants().Any(e => e.Attribute("id") is { } a && existingIds.Contains(prefix + a.Value))) prefix += "x";
            var ids = derived.Descendants().Where(e => e.Attribute("id") is not null).ToDictionary(e => e.Attribute("id")!.Value, e => prefix + e.Attribute("id")!.Value);
            foreach (var el in derived.DescendantsAndSelf()) foreach (var attribute in el.Attributes().ToArray())
            {
                if (attribute.Name.LocalName == "id" && ids.TryGetValue(attribute.Value, out var newId)) attribute.Value = newId;
                else if (attribute.Name.LocalName == "href" && ids.TryGetValue(attribute.Value.TrimStart('#'), out var reference)) attribute.Value = "#" + reference;
                else if (!attribute.Name.LocalName.StartsWith("data-", StringComparison.Ordinal)) attribute.Value = Regex.Replace(attribute.Value, "url\\(['\"]?#([^)'\" ]+)['\"]?\\)", match => ids.TryGetValue(match.Groups[1].Value, out var value) ? "url(#" + value + ")" : match.Value);
            }
            previous?.Remove(); owner.AddAfterSelf(derived);
            owner.SetAttributeValue("data-snap-stroke-shape", JsonSerializer.Serialize(new { attributes = attributes.EnumerateObject().ToDictionary(a => a.Name, a => (string?)owner.Attribute(a.Name)), width }));
            if (originalId is null) owner.SetAttributeValue("id", null);
            serial++; changed = true;
        }
        return changed ? SvgShape.Normalize(root.ToString(SaveOptions.DisableFormatting)) : svg;
    }
}
