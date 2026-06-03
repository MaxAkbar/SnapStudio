using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Export;

public enum ExportFormat
{
    Png,
    Jpeg,
    Pdf
}

public sealed record ExportRequest(
    DocumentId DocumentId,
    ExportFormat Format,
    string OutputPath,
    IReadOnlyDictionary<string, string> Options);

public sealed record ExportDestination(ExportFormat Format, string OutputPath);

public sealed record ExportResult(bool Succeeded, string? OutputPath, string? ErrorMessage)
{
    public static ExportResult Success(string outputPath) => new(true, outputPath, null);

    public static ExportResult Failed(string errorMessage) => new(false, null, errorMessage);
}

public sealed record ClipboardResult(bool Succeeded, string? ErrorMessage)
{
    public static ClipboardResult Success() => new(true, null);

    public static ClipboardResult Failed(string errorMessage) => new(false, errorMessage);
}
