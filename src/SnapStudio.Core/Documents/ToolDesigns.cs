using System.Text.Json.Serialization;

namespace SnapStudio.Core.Documents;

[JsonConverter(typeof(JsonStringEnumConverter<StrokePattern>))]
public enum StrokePattern { Solid, Dashed, Dotted, DashDot }

public sealed record ToolDesign(Guid Id, string Name, AnnotationKind Kind, AnnotationStyle Style)
{
    public string? Svg { get; init; }
}
public sealed record ToolDesignPack(int SchemaVersion, List<ToolDesign> Designs)
{
    public const int CurrentSchemaVersion = 2;
}

public interface IToolDesignStore
{
    Task<IReadOnlyList<ToolDesign>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<ToolDesign> designs, CancellationToken cancellationToken);
}

public interface IToolDesignFilePicker
{
    Task<string?> PickImportAsync(CancellationToken cancellationToken);
    Task<string?> PickExportAsync(CancellationToken cancellationToken);
    Task<string?> PickSvgImportAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    Task<string?> PickSvgExportAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

public static class ToolDesigns
{
    public static IReadOnlyList<ToolDesign> BuiltIns { get; } = new[] { AnnotationKind.Rectangle, AnnotationKind.Ellipse,
        AnnotationKind.Line, AnnotationKind.Arrow, AnnotationKind.Pen }.SelectMany(kind => new[] { StrokePattern.Dashed, StrokePattern.Dotted }
        .Select(pattern => new ToolDesign(new Guid($"10000000-0000-0000-0000-{(int)kind * 10 + (int)pattern:000000000000}"),
            $"{kind} · {pattern.ToString().ToLowerInvariant()}", kind,
            AnnotationStyle.Default with { Stroke = new(76, 194, 255, 255), StrokeThickness = 3, Pattern = pattern })))
        .Append(new ToolDesign(new("10000000-0000-0000-0000-000000000099"), "Star", AnnotationKind.Vector, AnnotationStyle.Default)
        { Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" preserveAspectRatio=\"none\"><path d=\"M50 3 L61 35 L96 36 L68 57 L78 92 L50 72 L22 92 L32 57 L4 36 L39 35 Z\" fill=\"#4CC2FF\"/></svg>" }).ToArray();

    public static bool SupportsPattern(AnnotationKind kind) => kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse
        or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Pen;

    public static double[] DashArray(StrokePattern pattern) => pattern switch
    {
        StrokePattern.Dashed => [4, 2],
        StrokePattern.Dotted => [1, 2],
        StrokePattern.DashDot => [4, 2, 1, 2],
        _ => []
    };

    public static ToolDesign Validate(ToolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.Id == Guid.Empty || string.IsNullOrWhiteSpace(design.Name) || design.Name.Trim().Length > 80)
            throw new InvalidDataException("A design needs an ID and a name of 1–80 characters.");
        if (!Enum.IsDefined(design.Kind) || design.Kind == AnnotationKind.Image || design.Style is not { } style)
            throw new InvalidDataException("Choose an existing drawing tool for this design.");
        if (!double.IsFinite(style.StrokeThickness) || style.StrokeThickness < 1 || !double.IsFinite(style.Opacity)
            || style.Opacity < 0.1 || style.Opacity > 1 || !double.IsFinite(style.CornerRadius) || style.CornerRadius < 0
            || !Enum.IsDefined(style.Pattern) || (!SupportsPattern(design.Kind) && style.Pattern != StrokePattern.Solid))
            throw new InvalidDataException("The design has an invalid width, opacity, corner radius, or stroke pattern.");
        if (design.Kind == AnnotationKind.Vector && string.IsNullOrWhiteSpace(design.Svg)) throw new InvalidDataException("A custom shape needs SVG artwork.");
        return design with { Name = design.Name.Trim(), Svg = design.Svg is null ? null : SvgShape.Normalize(design.Svg), Style = style with { FontFamily = string.IsNullOrWhiteSpace(style.FontFamily) ? AnnotationStyle.DefaultFontFamily : style.FontFamily.Trim() } };
    }

    public static List<ToolDesign> ValidateLibrary(IEnumerable<ToolDesign> designs)
    {
        var result = designs.Select(Validate).ToList();
        if (result.Select(item => item.Id).Distinct().Count() != result.Count
            || result.GroupBy(item => (item.Kind, item.Name.ToUpperInvariant())).Any(group => group.Count() > 1))
            throw new InvalidDataException("Design IDs and names within each tool must be unique.");
        return result;
    }
}
