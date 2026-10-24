using System.Text.Json;
using System.Text.RegularExpressions;

namespace SnapStudio.Core.Documents;

/// <summary>Capture labels stored with workspace metadata, independently of the source image.</summary>
public static class DocumentTags
{
    private const string PropertyName = "tags";
    public const int MaximumLength = 64;

    public static string[] Read(CaptureDocument document) =>
        document.Metadata.Properties.TryGetValue(PropertyName, out string? json) ? ReadJson(json) : [];

    public static string[] ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return Normalize(JsonSerializer.Deserialize<string[]>(json) ?? []); }
        catch (JsonException) { return []; }
        catch (ArgumentException) { return []; }
    }

    public static string[] Parse(string text) => Normalize(text.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    public static string[] Normalize(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? value in tags)
        {
            string tag = Regex.Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            if (tag.Length == 0) continue;
            if (tag.Length > MaximumLength) throw new ArgumentException($"Each tag can have up to {MaximumLength} characters.");
            if (tag.IndexOfAny([',', ';']) >= 0 || tag.Any(char.IsControl))
                throw new ArgumentException("Tags cannot contain commas, semicolons, or control characters.");
            if (seen.Add(tag)) result.Add(tag);
        }
        return result.ToArray();
    }

    public static void Set(CaptureDocument document, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(document);
        string[] normalized = Normalize(tags);
        var properties = new Dictionary<string, string>(document.Metadata.Properties);
        if (normalized.Length == 0) properties.Remove(PropertyName);
        else properties[PropertyName] = JsonSerializer.Serialize(normalized);
        document.Metadata = document.Metadata with { Properties = properties };
    }
}
