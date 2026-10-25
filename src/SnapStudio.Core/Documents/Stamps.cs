using System.Globalization;
using System.Xml.Linq;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public sealed record StampDefinition(Guid Id, string Name, string Category, string Svg)
{
    public StampAttribution? Attribution { get; init; }
    public override string ToString() => Name;
}
public sealed record StampPack(int SchemaVersion, List<StampDefinition> Stamps)
{
    public const int CurrentSchemaVersion = 1;
}

public interface IStampStore
{
    Task<IReadOnlyList<StampDefinition>> LoadAsync(CancellationToken token);
    Task SaveAsync(IReadOnlyList<StampDefinition> stamps, CancellationToken token);
}

public static class StampCatalog
{
    public static IReadOnlyList<StampDefinition> BuiltIns { get; } = CreateBuiltIns().Select(Validate).ToList().AsReadOnly();

    public static StampDefinition Validate(StampDefinition stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        if (stamp.Id == Guid.Empty || string.IsNullOrWhiteSpace(stamp.Name) || stamp.Name.Trim().Length > 80
            || string.IsNullOrWhiteSpace(stamp.Category) || stamp.Category.Trim().Length > 40)
            throw new InvalidDataException("A stamp needs an ID, a name of 1–80 characters, and a category of 1–40 characters.");
        return stamp with
        {
            Name = stamp.Name.Trim(),
            Category = stamp.Category.Trim(),
            Svg = SvgShape.Normalize(stamp.Svg),
            Attribution = stamp.Attribution is null ? null : ValidateAttribution(stamp.Attribution)
        };
    }

    public static StampAttribution ValidateAttribution(StampAttribution attribution)
    {
        static string Text(string? value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum || value.Any(char.IsControl))
                throw new InvalidDataException("The stamp's source or license information is invalid.");
            return value.Trim();
        }
        static string Link(string? value)
        {
            string link = Text(value, 2048);
            if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
                throw new InvalidDataException("Stamp source and license links must use HTTPS.");
            return link;
        }
        return attribution with
        {
            Provider = Text(attribution.Provider, 40),
            IconId = Text(attribution.IconId, 160),
            Collection = Text(attribution.Collection, 80),
            Author = Text(attribution.Author, 200),
            License = Text(attribution.License, 120),
            LicenseUrl = Link(attribution.LicenseUrl),
            SourceUrl = Link(attribution.SourceUrl)
        };
    }

    public static List<StampDefinition> ValidateLibrary(IEnumerable<StampDefinition> stamps)
    {
        var result = stamps.Select(Validate).ToList();
        if (result.Select(stamp => stamp.Id).Distinct().Count() != result.Count
            || result.GroupBy(stamp => (stamp.Category.ToUpperInvariant(), stamp.Name.ToUpperInvariant())).Any(group => group.Count() > 1))
            throw new InvalidDataException("Stamp IDs and names within a category must be unique.");
        return result;
    }

    public static StampDefinition? FindReusableOnlineStamp(IEnumerable<StampDefinition> stamps, OnlineStampIcon icon, string normalizedSvg) =>
        stamps.FirstOrDefault(stamp => stamp.Attribution == icon.Attribution && string.Equals(stamp.Svg, normalizedSvg, StringComparison.Ordinal));

    public static AnnotationObject Place(StampDefinition stamp, PointD center, SizeD canvas, double size)
    {
        stamp = Validate(stamp);
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(canvas.Width) || !double.IsFinite(canvas.Height)
            || canvas.Width <= 0 || canvas.Height <= 0 || !double.IsFinite(size) || size <= 0)
            throw new ArgumentException("Choose a finite stamp size and a point on a nonempty image.");
        XElement root = XElement.Parse(stamp.Svg);
        double width = double.Parse(root.Attribute("width")!.Value, CultureInfo.InvariantCulture);
        double height = double.Parse(root.Attribute("height")!.Value, CultureInfo.InvariantCulture);
        double scale = Math.Min(size / Math.Max(width, height), Math.Min(canvas.Width / width, canvas.Height / height));
        width *= scale; height *= scale;
        return new AnnotationObject
        {
            Kind = AnnotationKind.Vector,
            Bounds = new(Math.Clamp(center.X - width / 2, 0, Math.Max(0, canvas.Width - width)),
                Math.Clamp(center.Y - height / 2, 0, Math.Max(0, canvas.Height - height)), width, height),
            Vector = new(stamp.Svg, stamp.Name) { Attribution = stamp.Attribution },
            Style = AnnotationStyle.Default
        };
    }

    private static IEnumerable<StampDefinition> CreateBuiltIns()
    {
        static StampDefinition Stamp(int id, string name, string category, string artwork, string viewBox = "0 0 100 100") =>
            new(new Guid($"20000000-0000-0000-0000-{id:000000000000}"), name, category,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBox}\">{artwork}</svg>");
        yield return Stamp(1, "Approved", "Status", "<rect x=\"3\" y=\"3\" width=\"194\" height=\"64\" rx=\"10\" fill=\"#167D46\"/><path d=\"M17 35 L27 45 L45 24\" fill=\"none\" stroke=\"white\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><text x=\"58\" y=\"44\" font-family=\"Segoe UI\" font-size=\"22\" font-weight=\"bold\" fill=\"white\">APPROVED</text>", "0 0 200 70");
        yield return Stamp(2, "Rejected", "Status", "<rect x=\"3\" y=\"3\" width=\"194\" height=\"64\" rx=\"10\" fill=\"#C83838\"/><path d=\"M19 24 L40 45 M40 24 L19 45\" stroke=\"white\" stroke-width=\"6\" stroke-linecap=\"round\"/><text x=\"57\" y=\"44\" font-family=\"Segoe UI\" font-size=\"22\" font-weight=\"bold\" fill=\"white\">REJECTED</text>", "0 0 200 70");
        yield return Stamp(3, "Warning", "Status", "<path d=\"M50 5 L96 90 H4 Z\" fill=\"#FFCB45\" stroke=\"#7A4D00\" stroke-width=\"4\" stroke-linejoin=\"round\"/><path d=\"M50 33 V59\" stroke=\"#513700\" stroke-width=\"8\" stroke-linecap=\"round\"/><circle cx=\"50\" cy=\"74\" r=\"5\" fill=\"#513700\"/>");
        yield return Stamp(4, "Information", "Status", "<circle cx=\"50\" cy=\"50\" r=\"45\" fill=\"#1678CC\"/><circle cx=\"50\" cy=\"28\" r=\"6\" fill=\"white\"/><path d=\"M50 45 V75\" stroke=\"white\" stroke-width=\"10\" stroke-linecap=\"round\"/>");
        yield return Stamp(5, "Question", "Status", "<circle cx=\"50\" cy=\"50\" r=\"45\" fill=\"#8056BA\"/><path d=\"M35 33 C35 12 72 15 68 37 C67 46 50 47 50 60\" fill=\"none\" stroke=\"white\" stroke-width=\"8\" stroke-linecap=\"round\"/><circle cx=\"50\" cy=\"77\" r=\"5\" fill=\"white\"/>");
        yield return Stamp(6, "Check mark", "Symbols", "<circle cx=\"50\" cy=\"50\" r=\"45\" fill=\"#167D46\"/><path d=\"M24 51 L42 69 L77 31\" fill=\"none\" stroke=\"white\" stroke-width=\"10\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        yield return Stamp(7, "Cross", "Symbols", "<circle cx=\"50\" cy=\"50\" r=\"45\" fill=\"#C83838\"/><path d=\"M31 31 L69 69 M69 31 L31 69\" stroke=\"white\" stroke-width=\"10\" stroke-linecap=\"round\"/>");
        yield return Stamp(8, "Star", "Symbols", "<path d=\"M50 4 L62 35 L96 37 L70 58 L79 92 L50 73 L21 92 L30 58 L4 37 L38 35 Z\" fill=\"#FFC23F\" stroke=\"#B47600\" stroke-width=\"3\" stroke-linejoin=\"round\"/>");
        yield return Stamp(9, "Heart", "Symbols", "<path d=\"M50 88 C41 79 7 56 7 32 C7 7 37 3 50 24 C63 3 93 7 93 32 C93 56 59 79 50 88 Z\" fill=\"#E35068\"/>");
        yield return Stamp(10, "Flag", "Markers", "<path d=\"M20 92 V9\" stroke=\"#38445A\" stroke-width=\"7\" stroke-linecap=\"round\"/><path d=\"M23 10 H85 L69 33 L85 56 H23 Z\" fill=\"#1C86D1\"/>");
        yield return Stamp(11, "Pin", "Markers", "<path d=\"M50 96 C42 81 15 58 15 36 A35 35 0 1 1 85 36 C85 58 58 81 50 96 Z\" fill=\"#D94747\"/><circle cx=\"50\" cy=\"35\" r=\"13\" fill=\"white\"/>");
        for (int number = 1; number <= 5; number++)
            yield return Stamp(20 + number, $"Number {number}", "Numbers", $"<circle cx=\"50\" cy=\"50\" r=\"45\" fill=\"#1678CC\"/><text x=\"50\" y=\"69\" text-anchor=\"middle\" font-family=\"Segoe UI\" font-size=\"54\" font-weight=\"bold\" fill=\"white\">{number}</text>");
    }
}
