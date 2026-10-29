using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Rendering;
using Windows.UI;

namespace SnapStudio.App.ViewModels.Items;

public sealed class AnnotationOverlayItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    internal int VisualVersion { get; private set; }
    public StrokePattern Pattern { get; private set; }
    public DoubleCollection StrokeDashes => [.. ToolDesigns.DashArray(Pattern)];
    public bool ShowTransformHandles { get; private set; } = true;
    public IReadOnlyList<RectD>? LayerClipRectangles { get; private set; }
    public CustomStrokePreview? CustomStroke { get; private set; }

    internal void UpdateFrom(AnnotationOverlayItem next)
    {
        bool drawingChanged = (ShowTransformHandles != next.ShowTransformHandles && (IsSelected || next.IsSelected))
            || (LayerClipRectangles is null) != (next.LayerClipRectangles is null)
            || LayerClipRectangles is not null && next.LayerClipRectangles is not null && !LayerClipRectangles.SequenceEqual(next.LayerClipRectangles)
            || CustomStroke != next.CustomStroke || Pattern != next.Pattern || Kind != next.Kind || Width != next.Width || Height != next.Height
            || StartX != next.StartX || StartY != next.StartY || EndX != next.EndX || EndY != next.EndY
            || Text != next.Text || !SameColor(StrokeBrush, next.StrokeBrush) || !SameColor(FillBrush, next.FillBrush)
            || !SameColor(TextBrush, next.TextBrush) || StrokeThicknessValue != next.StrokeThicknessValue
            || CornerRadius != next.CornerRadius || FontSize != next.FontSize || FontFamily != next.FontFamily
            || Opacity != next.Opacity || IsSelected != next.IsSelected || Vector != next.Vector || !ReferenceEquals(Image, next.Image)
            || !StrokePoints.SequenceEqual(next.StrokePoints) || !FillRectangles.SequenceEqual(next.FillRectangles);
        bool rowChanged = Kind != next.Kind || Text != next.Text || IsVisible != next.IsVisible
            || IsSelected != next.IsSelected || CanMoveUp != next.CanMoveUp || CanMoveDown != next.CanMoveDown;
        Left = next.Left; Top = next.Top; Width = next.Width; Height = next.Height;
        StartX = next.StartX; StartY = next.StartY; EndX = next.EndX; EndY = next.EndY;
        Kind = next.Kind; Text = next.Text; StrokeBrush = next.StrokeBrush; FillBrush = next.FillBrush;
        TextBrush = next.TextBrush; StrokeThickness = next.StrokeThickness; StrokeThicknessValue = next.StrokeThicknessValue;
        CornerRadius = next.CornerRadius; FontSize = next.FontSize; FontFamily = next.FontFamily; Opacity = next.Opacity;
        IsVisible = next.IsVisible; IsSelected = next.IsSelected; Image = next.Image;
        Vector = next.Vector;
        StrokePoints = next.StrokePoints; FillRectangles = next.FillRectangles;
        CanMoveUp = next.CanMoveUp; CanMoveDown = next.CanMoveDown;
        Pattern = next.Pattern;
        ShowTransformHandles = next.ShowTransformHandles;
        LayerClipRectangles = next.LayerClipRectangles;
        CustomStroke = next.CustomStroke;
        if (drawingChanged) VisualVersion++;
        if (rowChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static bool SameColor(Brush first, Brush second) =>
        first is SolidColorBrush a && second is SolidColorBrush b && a.Color == b.Color;

    public AnnotationImage? Image { get; private set; }
    public AnnotationVector? Vector { get; private set; }

    public string FontFamily { get; private set; } = AnnotationStyle.DefaultFontFamily;

    public IReadOnlyList<PointD> StrokePoints { get; private set; } = [];

    public IReadOnlyList<RectD> FillRectangles { get; private set; } = [];

    public bool CanMoveUp { get; private set; }

    public bool CanMoveDown { get; private set; }
    private AnnotationOverlayItem(
        AnnotationKind kind,
        Guid id,
        double left,
        double top,
        double width,
        double height,
        double startX,
        double startY,
        double endX,
        double endY,
        string text,
        Brush strokeBrush,
        Brush fillBrush,
        Brush textBrush,
        Thickness strokeThickness,
        double strokeThicknessValue,
        double cornerRadius,
        double fontSize,
        double opacity,
        bool isVisible,
        bool isSelected)
    {
        Kind = kind;
        Id = id;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
        Text = text;
        StrokeBrush = strokeBrush;
        FillBrush = fillBrush;
        TextBrush = textBrush;
        StrokeThickness = strokeThickness;
        StrokeThicknessValue = strokeThicknessValue;
        CornerRadius = cornerRadius;
        FontSize = fontSize;
        Opacity = opacity;
        IsVisible = isVisible;
        IsSelected = isSelected;
    }

    public AnnotationKind Kind { get; private set; }

    public Guid Id { get; }

    public double Left { get; private set; }

    public double Top { get; private set; }

    public double Width { get; private set; }

    public double Height { get; private set; }

    public double StartX { get; private set; }

    public double StartY { get; private set; }

    public double EndX { get; private set; }

    public double EndY { get; private set; }

    public string Text { get; private set; }

    public Brush StrokeBrush { get; private set; }

    public Brush FillBrush { get; private set; }

    public Brush TextBrush { get; private set; }

    public Thickness StrokeThickness { get; private set; }

    public double StrokeThicknessValue { get; private set; }

    public double CornerRadius { get; private set; }

    public double FontSize { get; private set; }

    public double Opacity { get; private set; }

    public bool IsVisible { get; private set; }

    public bool IsSelected { get; private set; }

    public string LayerLabel => Kind == AnnotationKind.Vector ? Vector?.Name ?? "SVG shape" : Kind is AnnotationKind.Text or AnnotationKind.Image
        ? Text
        : Kind == AnnotationKind.Highlight ? "Highlighter" : Kind.ToString();

    public string LayerGlyph => Kind switch
    {
        AnnotationKind.Arrow => "↗",
        AnnotationKind.Line => "╱",
        AnnotationKind.Rectangle => "□",
        AnnotationKind.Ellipse => "○",
        AnnotationKind.Text => "T",
        AnnotationKind.Image => "\u2196",
        AnnotationKind.Vector => "◇",
        AnnotationKind.Highlight => "▤",
        AnnotationKind.Blur => "◌",
        AnnotationKind.Pen => "✎",
        AnnotationKind.Fill => "▧",
        _ => "•"
    };

    public Visibility HiddenVisibility => IsVisible ? Visibility.Collapsed : Visibility.Visible;

    public string ObjectAutomationName => $"Select {LayerLabel} object";

    public string ObjectSelectionHelpText => $"{LayerLabel}. Right-click for object actions. Use V and arrow keys to move, Shift+Arrow for ten pixels, Ctrl+J to duplicate, and Delete to remove.";

    public string ObjectVisibilityAutomationName => IsVisible
        ? $"Hide {LayerLabel} object"
        : $"Show {LayerLabel} object";

    public Visibility SelectionVisibility => IsSelected
        ? Visibility.Visible
        : Visibility.Collapsed;

    public static AnnotationOverlayItem FromAnnotation(
        AnnotationObject annotation,
        double zoom,
        Guid? selectedAnnotationId,
        bool canMoveUp = false,
        bool canMoveDown = false,
        bool showTransformHandles = true,
        IReadOnlyList<RectD>? layerClip = null)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        layerClip = ObjectClipping.Combine(layerClip, ObjectClipping.GetRectangles(annotation));

        RectD bounds = annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow
            ? CreateDirectedDisplayBounds(annotation.Bounds, zoom)
            : Normalize(annotation.Bounds);
        bool isSelected = selectedAnnotationId == annotation.Id;
        double strokeThickness = Math.Max(1, annotation.Style.StrokeThickness * zoom);
        double opacity = Math.Clamp(annotation.Style.Opacity, 0.1, 1);

        return new AnnotationOverlayItem(
            annotation.Kind,
            annotation.Id,
            bounds.X * zoom,
            bounds.Y * zoom,
            Math.Max(1, bounds.Width * zoom),
            Math.Max(1, bounds.Height * zoom),
            CalculateRelativeStartX(annotation, bounds, zoom),
            CalculateRelativeStartY(annotation, bounds, zoom),
            CalculateRelativeEndX(annotation, bounds, zoom),
            CalculateRelativeEndY(annotation, bounds, zoom),
            string.IsNullOrWhiteSpace(annotation.Text) ? "Text" : annotation.Text,
            ToBrush(annotation.Style.Pattern == StrokePattern.Custom ? SnapStudio.Core.Primitives.ColorRgba.Transparent : annotation.Style.Stroke, opacity),
            ToBrush(annotation.Style.Fill, opacity),
            ToBrush(annotation.Style.Text, opacity),
            new Thickness(strokeThickness),
            strokeThickness,
            Math.Max(0, annotation.Style.CornerRadius * zoom),
            CalculateFontSize(annotation.Style.StrokeThickness, annotation.Kind, zoom),
            opacity,
            annotation.IsVisible,
            isSelected)
        {
            Vector = annotation.Vector,
            CustomStroke = CustomStrokeRenderer.Preview(annotation, zoom),
            Image = annotation.Kind == AnnotationKind.Vector && annotation.Vector is not null
                ? SvgRasterizer.Preview(annotation.Style.Pattern == StrokePattern.Custom ? CustomStrokeRenderer.HideSvgStrokes(annotation.Vector.Svg) : annotation.Vector.Svg, Math.Max(1, bounds.Width * zoom), Math.Max(1, bounds.Height * zoom)) : annotation.Image,
            Pattern = annotation.Style.Pattern,
            ShowTransformHandles = showTransformHandles,
            LayerClipRectangles = layerClip?.Select(r => new RectD((r.X - bounds.X) * zoom, (r.Y - bounds.Y) * zoom, r.Width * zoom, r.Height * zoom)).ToArray(),
            FontFamily = SystemFontCatalog.ResolveFamily(annotation.Style.FontFamily),
            CanMoveUp = canMoveUp,
            CanMoveDown = canMoveDown,
            FillRectangles = annotation.FillRectangles.Select(rectangle => new RectD(rectangle.X * bounds.Width * zoom,
                rectangle.Y * bounds.Height * zoom, rectangle.Width * bounds.Width * zoom, rectangle.Height * bounds.Height * zoom)).ToArray(),
            StrokePoints = annotation.StrokePoints.Select(point => new PointD(point.X * bounds.Width * zoom, point.Y * bounds.Height * zoom)).ToArray()
        };
    }

    private static double CalculateRelativeStartX(
        AnnotationObject annotation,
        RectD displayBounds,
        double zoom)
    {
        return annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow
            ? annotation.Bounds.X * zoom - displayBounds.X * zoom
            : 0;
    }

    private static double CalculateRelativeStartY(
        AnnotationObject annotation,
        RectD displayBounds,
        double zoom)
    {
        return annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow
            ? annotation.Bounds.Y * zoom - displayBounds.Y * zoom
            : 0;
    }

    private static double CalculateRelativeEndX(
        AnnotationObject annotation,
        RectD displayBounds,
        double zoom)
    {
        return annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow
            ? annotation.Bounds.Right * zoom - displayBounds.X * zoom
            : displayBounds.Width * zoom;
    }

    private static double CalculateRelativeEndY(
        AnnotationObject annotation,
        RectD displayBounds,
        double zoom)
    {
        return annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow
            ? annotation.Bounds.Bottom * zoom - displayBounds.Y * zoom
            : displayBounds.Height * zoom;
    }

    private static RectD CreateDirectedDisplayBounds(RectD sourceBounds, double zoom)
    {
        double startX = sourceBounds.X * zoom;
        double startY = sourceBounds.Y * zoom;
        double endX = sourceBounds.Right * zoom;
        double endY = sourceBounds.Bottom * zoom;
        double left = Math.Min(startX, endX) / zoom;
        double top = Math.Min(startY, endY) / zoom;
        double right = Math.Max(startX, endX) / zoom;
        double bottom = Math.Max(startY, endY) / zoom;

        return new RectD(left, top, right - left, bottom - top);
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static double CalculateFontSize(
        double styleSize,
        AnnotationKind annotationKind,
        double zoom)
    {
        if (annotationKind == AnnotationKind.Text)
        {
            return Math.Max(0.1, styleSize * zoom);
        }

        return Math.Max(10, (styleSize * 4 + 8) * zoom);
    }

    private static SolidColorBrush ToBrush(ColorRgba color, double opacity)
    {
        byte alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}
