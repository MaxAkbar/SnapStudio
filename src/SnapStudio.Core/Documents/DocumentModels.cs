using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public enum AnnotationKind
{
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Text,
    Highlight,
    Blur
}

public enum DestructiveOperationKind
{
    Crop,
    Resize,
    RasterBlur,
    Pixelate
}

public sealed class CaptureDocument
{
    public const int CurrentSchemaVersion = 2;

    public DocumentId Id { get; set; } = DocumentId.New();

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ImageAsset SourceImage { get; set; } = new(string.Empty, 0, 0, ImagePixelFormat.Unknown);

    public DocumentMetadata Metadata { get; set; } = DocumentMetadata.Empty;

    public List<AnnotationObject> Annotations { get; set; } = [];

    public List<AnnotationLayer> Layers { get; set; } = [];

    public List<DestructiveEditOperation> DestructiveOperations { get; set; } = [];

    public ExportSettings ExportSettings { get; set; } = ExportSettings.Default;
}

public sealed record DocumentMetadata(
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    Dictionary<string, string> Properties)
{
    public static DocumentMetadata Empty => new(default, default, []);
}

public sealed class AnnotationObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AnnotationKind Kind { get; set; }

    public Guid? LayerId { get; set; }

    public bool IsVisible { get; set; } = true;

    public RectD Bounds { get; set; }

    public string? Text { get; set; }

    public AnnotationStyle Style { get; set; } = AnnotationStyle.Default;
}

public sealed class AnnotationLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Layer 1";

    public bool IsVisible { get; set; } = true;
}

public sealed record AnnotationStyle(
    ColorRgba Stroke,
    ColorRgba Fill,
    ColorRgba Text,
    double StrokeThickness,
    double Opacity,
    double CornerRadius = 0)
{
    public static AnnotationStyle Default => new(
        ColorRgba.Black,
        ColorRgba.Transparent,
        ColorRgba.Black,
        2,
        1);
}

public sealed record DestructiveEditOperation(
    Guid Id,
    DestructiveOperationKind Kind,
    RectD? Bounds,
    Dictionary<string, string> Parameters);

public sealed record ExportSettings(string? LastExportDirectory, string? LastExportFormat)
{
    public static ExportSettings Default => new(null, null);
}

public sealed record DocumentSummary(
    DocumentId Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    string SourceImagePath,
    int AnnotationCount,
    string? ThumbnailPath = null,
    string SourceKind = "capture");

public sealed record DocumentThumbnailRequest(
    DocumentId DocumentId,
    DateTimeOffset ModifiedAtUtc,
    int MaximumPixelSize);

public sealed record DocumentThumbnailResult(
    string? ThumbnailPath,
    bool WasGenerated,
    string? ErrorMessage)
{
    public bool Succeeded => !string.IsNullOrWhiteSpace(ThumbnailPath);

    public static DocumentThumbnailResult Success(
        string thumbnailPath,
        bool wasGenerated) => new(thumbnailPath, wasGenerated, null);

    public static DocumentThumbnailResult Failed(string errorMessage) => new(null, false, errorMessage);
}
