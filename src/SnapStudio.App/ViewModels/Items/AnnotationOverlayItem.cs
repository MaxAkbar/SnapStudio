using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.UI;

namespace SnapStudio.App.ViewModels.Items;

public sealed class AnnotationOverlayItem
{
    public AnnotationImage? Image { get; private init; }

    public IReadOnlyList<PointD> StrokePoints { get; private init; } = [];

    public IReadOnlyList<RectD> FillRectangles { get; private init; } = [];

    public bool CanMoveUp { get; private init; }

    public bool CanMoveDown { get; private init; }
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

    public AnnotationKind Kind { get; }

    public Guid Id { get; }

    public double Left { get; }

    public double Top { get; }

    public double Width { get; }

    public double Height { get; }

    public double StartX { get; }

    public double StartY { get; }

    public double EndX { get; }

    public double EndY { get; }

    public string Text { get; }

    public Brush StrokeBrush { get; }

    public Brush FillBrush { get; }

    public Brush TextBrush { get; }

    public Thickness StrokeThickness { get; }

    public double StrokeThicknessValue { get; }

    public double CornerRadius { get; }

    public double FontSize { get; }

    public double Opacity { get; }

    public bool IsVisible { get; }

    public bool IsSelected { get; }

    public string LayerLabel => Kind is AnnotationKind.Text or AnnotationKind.Image
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
        AnnotationKind.Highlight => "▤",
        AnnotationKind.Blur => "◌",
        AnnotationKind.Pen => "✎",
        AnnotationKind.Fill => "▧",
        _ => "•"
    };

    public Brush LayerBackgroundBrush => IsSelected
        ? new SolidColorBrush(Color.FromArgb(255, 36, 68, 94))
        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public Brush LayerBorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromArgb(255, 76, 194, 255))
        : new SolidColorBrush(Color.FromArgb(255, 48, 59, 71));

    public Visibility HiddenVisibility => IsVisible ? Visibility.Collapsed : Visibility.Visible;

    public string ObjectAutomationName => $"Select {LayerLabel} object";

    public string ObjectSelectionHelpText => $"{LayerLabel}. Right-click to move this object up or down within its layer.";

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
        bool canMoveDown = false)
    {
        ArgumentNullException.ThrowIfNull(annotation);

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
            ToBrush(annotation.Style.Stroke, opacity),
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
            Image = annotation.Image,
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
        if (annotationKind == AnnotationKind.Text && styleSize > 12)
        {
            return Math.Max(10, styleSize * zoom);
        }

        return Math.Max(10, (styleSize * 4 + 8) * zoom);
    }

    private static SolidColorBrush ToBrush(ColorRgba color, double opacity)
    {
        byte alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}
