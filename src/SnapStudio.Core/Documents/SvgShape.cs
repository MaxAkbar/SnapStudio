using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SnapStudio.Core.Documents;

public static partial class SvgShape
{
    public const int MaximumCharacters = 1_000_000;
    private const string Namespace = "http://www.w3.org/2000/svg";
    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    { "svg", "g", "defs", "title", "desc", "metadata", "rect", "circle", "ellipse", "line", "path", "polyline", "polygon", "text", "tspan", "textPath", "linearGradient", "radialGradient", "stop", "clipPath", "mask", "pattern", "symbol", "use", "switch", "style" };
    private static readonly HashSet<string> DrawingElements = new(StringComparer.Ordinal)
    { "rect", "circle", "ellipse", "line", "path", "polyline", "polygon", "text", "use" };

    public static string Normalize(string svg, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(svg) || svg.Length > MaximumCharacters) throw new InvalidDataException("Enter an SVG document smaller than 1 MB.");
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumCharacters });
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException error) { throw new InvalidDataException("The SVG is not valid XML: " + error.Message, error); }
        if (document.Nodes().OfType<XProcessingInstruction>().Any()) throw new InvalidDataException("External SVG stylesheets are not supported.");
        XElement root = document.Root ?? throw new InvalidDataException("The SVG has no root element.");
        if (root.Name.LocalName != "svg" || root.Name.NamespaceName is not ("" or Namespace)) throw new InvalidDataException("The document must have an SVG root element.");
        foreach (XElement metadata in root.Descendants().Where(element => element.Name.LocalName == "metadata").ToArray()) metadata.Remove();
        foreach (XElement editorData in root.Descendants().Where(element => element.Name.NamespaceName is not ("" or Namespace)).ToArray()) editorData.Remove();
        XElement[] elements = root.DescendantsAndSelf().ToArray();
        if (elements.Length > 4096 || elements.Any(element => element.Ancestors().Count() > 32)) throw new InvalidDataException("This SVG is too complex to use as a tool (maximum 4096 elements and 32 nested groups).");
        var ids = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (XElement element in elements)
        {
            if (!Elements.Contains(element.Name.LocalName)) throw new InvalidDataException($"SVG element '{element.Name.LocalName}' is not supported. Use paths, shapes, groups, text, gradients, or clipping paths.");
            element.Name = XName.Get(element.Name.LocalName, Namespace);
            foreach (XAttribute attribute in element.Attributes().ToArray())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                string name = attribute.Name.LocalName;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name == "base") throw new InvalidDataException("SVG event handlers and external base URLs are not supported.");
                if (attribute.Name.NamespaceName.Length > 0 && attribute.Name.NamespaceName is not ("http://www.w3.org/1999/xlink" or "http://www.w3.org/XML/1998/namespace")) { attribute.Remove(); continue; }
                if (name == "href" && (!attribute.Value.StartsWith('#') || attribute.Value.Length < 2)) throw new InvalidDataException("SVG references must point to artwork within the same document.");
                ValidateCss(attribute.Value);
            }
            if (element.Name.LocalName == "style") ValidateCss(element.Value);
            if ((string?)element.Attribute("id") is { Length: > 0 } id && !ids.TryAdd(id, element)) throw new InvalidDataException("SVG element IDs must be unique.");
        }
        if (!allowEmpty && !elements.Any(element => DrawingElements.Contains(element.Name.LocalName) && !element.Ancestors().Any(parent => parent.Name.LocalName is "defs" or "clipPath" or "mask" or "pattern" or "symbol")))
            throw new InvalidDataException("Add at least one shape or path before saving the tool.");
        var completed = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id, HashSet<string> visiting)
        {
            if (completed.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("The SVG contains a circular reference.");
            foreach (string referenced in References(ids[id]))
            {
                if (!ids.ContainsKey(referenced)) throw new InvalidDataException("An SVG reference points to a missing element.");
                Visit(referenced, visiting);
            }
            visiting.Remove(id); completed.Add(id);
        }
        foreach (string id in ids.Keys) Visit(id, []);
        foreach (string referenced in References(root))
            if (!ids.ContainsKey(referenced)) throw new InvalidDataException("An SVG reference points to a missing element.");
        string[] viewBox = ((string?)root.Attribute("viewBox") ?? "").Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (viewBox.Length == 0)
        {
            double width = ReadDimension((string?)root.Attribute("width")), height = ReadDimension((string?)root.Attribute("height"));
            viewBox = ["0", "0", width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture)];
        }
        double[] numbers = viewBox.Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : double.NaN).ToArray();
        if (numbers.Length != 4 || numbers.Any(value => !double.IsFinite(value) || Math.Abs(value) > 1_000_000) || numbers[2] <= 0 || numbers[3] <= 0)
            throw new InvalidDataException("The SVG needs a valid viewBox with positive dimensions.");
        root.SetAttributeValue("viewBox", string.Join(" ", numbers.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        root.SetAttributeValue("width", numbers[2].ToString(CultureInfo.InvariantCulture));
        root.SetAttributeValue("height", numbers[3].ToString(CultureInfo.InvariantCulture));
        root.SetAttributeValue("xmlns", Namespace);
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static double ReadDimension(string? value)
    {
        if (value is null) return 512;
        string number = value.Trim().EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value.Trim()[..^2] : value;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)) throw new InvalidDataException("SVG files without a viewBox need pixel width and height values.");
        return result;
    }

    private static IEnumerable<string> References(XElement element)
    {
        foreach (XElement child in element.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in child.Attributes().Where(item => !item.IsNamespaceDeclaration))
            {
                if (attribute.Name.LocalName == "href") yield return attribute.Value[1..];
                foreach (Match match in CssUrl().Matches(attribute.Value)) yield return match.Groups[1].Value.Trim().Trim('\'', '"')[1..];
            }
            if (child.Name.LocalName == "style")
                foreach (Match match in CssUrl().Matches(child.Value)) yield return match.Groups[1].Value.Trim().Trim('\'', '"')[1..];
        }
    }

    private static void ValidateCss(string value)
    {
        if (value.Contains("@import", StringComparison.OrdinalIgnoreCase) || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("expression(", StringComparison.OrdinalIgnoreCase) || value.Contains('\\')) throw new InvalidDataException("External SVG resources and CSS imports are not supported.");
        foreach (Match match in CssUrl().Matches(value))
            if (!match.Groups[1].Value.Trim().Trim('\'', '"').StartsWith('#')) throw new InvalidDataException("SVG resources must be defined inside the document.");
    }

    [GeneratedRegex(@"url\s*\(\s*([^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrl();
}
