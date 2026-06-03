using System.Text.RegularExpressions;

namespace SnapStudio.Core.Diagnostics;

public sealed partial class DefaultSensitiveDataRedactor : ISensitiveDataRedactor
{
    public string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        string redacted = WindowsPathRegex().Replace(value, "<path>");
        redacted = UrlRegex().Replace(redacted, "<url>");
        redacted = EmailRegex().Replace(redacted, "<email>");

        return redacted;
    }

    public IReadOnlyDictionary<string, string> RedactProperties(
        IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.ToDictionary(
            pair => pair.Key,
            pair => Redact(pair.Value));
    }

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\s\r\n]+")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:https?|ftp)://[^\s""'<>)]*[^\s""'<>).,;:!?]", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
