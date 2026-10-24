using System.Drawing;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Rendering;

public static class TextAnnotationLayout
{
    public const double Padding = 2;
    public const string FontFamily = AnnotationStyle.DefaultFontFamily;

    public static string DisplayText(string? text) => string.IsNullOrWhiteSpace(text) ? "Text" : text;

    public static StringFormat CreateFormat() => new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces
    };

    public static SizeD Measure(string? text, double fontSize, string? fontFamily = null)
    {
        using var bitmap = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = SystemFontCatalog.CreateFont(fontFamily, Math.Max(1, fontSize));
        using StringFormat format = CreateFormat();
        SizeF measured = graphics.MeasureString(DisplayText(text), font, new PointF(0, 0), format);
        return new SizeD(Math.Ceiling(measured.Width) + Padding * 2,
            Math.Ceiling(Math.Max(measured.Height, font.GetHeight(graphics))) + Padding * 2);
    }

    public static RectD FitBounds(RectD bounds, string? text, double fontSize, string? fontFamily = null)
    {
        SizeD size = Measure(text, fontSize, fontFamily);
        return bounds with { Width = size.Width, Height = size.Height };
    }

    public static (RectD Bounds, AnnotationStyle Style) Resize(AnnotationObject annotation, RectD requested)
    {
        RectD before = annotation.Bounds;
        bool widthChanged = Math.Abs(requested.Width - before.Width) > 0.01;
        bool heightChanged = Math.Abs(requested.Height - before.Height) > 0.01;
        double scaleX = requested.Width / Math.Max(1, before.Width);
        double scaleY = requested.Height / Math.Max(1, before.Height);
        double scale = widthChanged && heightChanged ? Math.Min(scaleX, scaleY) : widthChanged ? scaleX : heightChanged ? scaleY : 1;
        AnnotationStyle style = annotation.Style with { StrokeThickness = Math.Max(1, annotation.Style.StrokeThickness * scale) };
        RectD fitted = FitBounds(requested, annotation.Text, style.StrokeThickness, style.FontFamily);
        if (Math.Abs(requested.Right - before.Right) < 0.01 && Math.Abs(requested.X - before.X) > 0.01)
            fitted = fitted with { X = requested.Right - fitted.Width };
        if (Math.Abs(requested.Bottom - before.Bottom) < 0.01 && Math.Abs(requested.Y - before.Y) > 0.01)
            fitted = fitted with { Y = requested.Bottom - fitted.Height };
        return (fitted, style);
    }
}
