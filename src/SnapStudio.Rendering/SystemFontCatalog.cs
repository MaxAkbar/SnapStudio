using System.Drawing;
using System.Drawing.Text;
using SnapStudio.Core.Documents;

namespace SnapStudio.Rendering;

public static class SystemFontCatalog
{
    private static readonly Lazy<IReadOnlyList<string>> InstalledFamilies = new(LoadFamilies);

    public static IReadOnlyList<string> Families => InstalledFamilies.Value;

    public static string ResolveFamily(string? requested) =>
        Families.FirstOrDefault(name => string.Equals(name, requested?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Families.FirstOrDefault(name => name == AnnotationStyle.DefaultFontFamily)
        ?? Families.FirstOrDefault(name => name == FontFamily.GenericSansSerif.Name)
        ?? Families[0];

    public static Font CreateFont(string? requested, double size)
    {
        using var family = new FontFamily(ResolveFamily(requested));
        FontStyle style = new[] { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic }
            .First(family.IsStyleAvailable);
        return new Font(family, (float)Math.Max(0.1, size), style, GraphicsUnit.Pixel);
    }

    private static IReadOnlyList<string> LoadFamilies()
    {
        using var collection = new InstalledFontCollection();
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (FontFamily family in collection.Families)
        {
            using (family)
            {
                if (!string.IsNullOrWhiteSpace(family.Name) && !family.Name.StartsWith('@')
                    && new[] { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic }.Any(family.IsStyleAvailable))
                    names.Add(family.Name);
            }
        }
        if (names.Count == 0) names.Add(FontFamily.GenericSansSerif.Name);
        return names.ToList().AsReadOnly();
    }
}
