using System.Globalization;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class SvgToolTemplates
{
    public const string Blank = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" preserveAspectRatio=\"none\"></svg>";

    public static string Create(AnnotationKind kind, AnnotationStyle style)
    {
        string Color(ColorRgba value) => value.A == 0 ? "none" : $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
        string shape = kind switch
        {
            AnnotationKind.Rectangle => $"<rect x='24' y='24' width='208' height='208' rx='{Number(style.CornerRadius)}'/>",
            AnnotationKind.Ellipse => "<ellipse cx='128' cy='128' rx='104' ry='88'/>",
            AnnotationKind.Line => "<path d='M24 216 L232 40'/>",
            AnnotationKind.Arrow => "<path d='M24 128 H180 M144 80 L216 128 L144 176' fill='none'/>",
            AnnotationKind.Text => $"<text x='24' y='152' fill='{Color(style.Text)}' stroke='none' font-family='{(global::System.Security.SecurityElement.Escape(style.FontFamily))}' font-size='{Number(Math.Max(24, style.StrokeThickness))}'>Text</text>",
            AnnotationKind.Pen => "<path d='M24 200 C48 32 96 48 128 128 S208 224 232 56' fill='none'/>",
            AnnotationKind.Highlight or AnnotationKind.Fill => $"<rect x='24' y='64' width='208' height='128' fill='{Color(style.Fill)}' stroke='none'/>",
            _ => ""
        };
        if (shape.Length == 0) return Blank;
        string dash = style.Pattern == StrokePattern.Solid ? "" : $"stroke-dasharray='{string.Join(" ", ToolDesigns.DashArray(style.Pattern).Select(value => Number(value * style.StrokeThickness)))}'";
        return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 256 256' preserveAspectRatio='none'><g stroke='{Color(style.Stroke)}' stroke-width='{Number(style.StrokeThickness)}' stroke-opacity='{Number(style.Stroke.A / 255d)}' fill='{Color(style.Fill)}' fill-opacity='{Number(style.Fill.A / 255d)}' opacity='{Number(style.Opacity)}' {dash}>{shape}</g></svg>";
    }
}
