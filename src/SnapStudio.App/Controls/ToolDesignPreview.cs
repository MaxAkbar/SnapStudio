using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class ToolDesignPreview : UserControl
{
    private readonly Canvas _canvas = new() { Width = 220, Height = 105 };
    public ToolDesignPreview()
    {
        Height = 110;
        var background = new Canvas { Width = 220, Height = 105, Background = new SolidColorBrush(Color.FromArgb(255, 190, 195, 200)) };
        for (int y = 0; y < 105; y += 15)
            for (int x = 0; x < 220; x += 15)
                if ((x / 15 + y / 15) % 2 == 0)
                {
                    var tile = new Rectangle { Width = Math.Min(15, 220 - x), Height = 15, Fill = new SolidColorBrush(Color.FromArgb(255, 225, 229, 233)) };
                    Canvas.SetLeft(tile, x); Canvas.SetTop(tile, y); background.Children.Add(tile);
                }
        background.Children.Add(_canvas);
        Content = new Viewbox { Child = background, Stretch = Stretch.Uniform };
        IsHitTestVisible = false;
    }
    public void Update(ToolDesign design)
    {
        _canvas.Children.Clear(); AnnotationStyle style = design.Style;
        Brush Brush(ColorRgba color) => new SolidColorBrush(Color.FromArgb((byte)(color.A * style.Opacity), color.R, color.G, color.B));
        Shape? shape = design.Kind switch
        {
            AnnotationKind.Rectangle => new Rectangle { Width = 180, Height = 70, RadiusX = style.CornerRadius, RadiusY = style.CornerRadius },
            AnnotationKind.Ellipse => new Ellipse { Width = 180, Height = 70 },
            AnnotationKind.Highlight or AnnotationKind.Fill => new Rectangle { Width = 180, Height = 70 },
            AnnotationKind.Line => new Line { X1 = 0, Y1 = 70, X2 = 180, Y2 = 0 },
            AnnotationKind.Pen => new Polyline { Points = [new Point(0, 55), new Point(35, 15), new Point(75, 60), new Point(120, 10), new Point(180, 50)] },
            _ => null
        };
        if (shape is not null)
        {
            shape.Stroke = design.Kind is AnnotationKind.Highlight or AnnotationKind.Fill ? null : Brush(style.Stroke);
            shape.Fill = design.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Highlight or AnnotationKind.Fill ? Brush(style.Fill) : null;
            shape.StrokeThickness = Math.Min(18, style.StrokeThickness); shape.StrokeDashArray = [.. ToolDesigns.DashArray(style.Pattern)];
            Canvas.SetLeft(shape, 20); Canvas.SetTop(shape, 18); _canvas.Children.Add(shape);
        }
        else if (design.Kind == AnnotationKind.Arrow)
        {
            var outline = ArrowGeometry.CreateOutline(new(20, 85), new(200, 18), Math.Min(18, style.StrokeThickness));
            if (style.Pattern == StrokePattern.Solid)
                _canvas.Children.Add(new Polygon { Points = [.. outline.Select(point => new Point(point.X, point.Y))], Fill = Brush(style.Stroke) });
            else
            {
                _canvas.Children.Add(new Line
                {
                    X1 = 20,
                    Y1 = 85,
                    X2 = (outline[2].X + outline[4].X) / 2,
                    Y2 = (outline[2].Y + outline[4].Y) / 2,
                    Stroke = Brush(style.Stroke),
                    StrokeThickness = Math.Min(18, style.StrokeThickness),
                    StrokeDashArray = [.. ToolDesigns.DashArray(style.Pattern)]
                });
                _canvas.Children.Add(new Polygon { Points = [new(outline[2].X, outline[2].Y), new(outline[3].X, outline[3].Y), new(outline[4].X, outline[4].Y)], Fill = Brush(style.Stroke) });
            }
        }
        else
        {
            var label = new TextBlock
            {
                Text = design.Kind == AnnotationKind.Blur ? $"Blur strength: {style.Opacity:P0}" : "Design",
                Foreground = Brush(design.Kind == AnnotationKind.Blur ? ColorRgba.Black : style.Text),
                FontFamily = new FontFamily(style.FontFamily),
                FontSize = design.Kind == AnnotationKind.Blur ? 20 : Math.Clamp(style.StrokeThickness, 10, 48)
            };
            Canvas.SetLeft(label, 20); Canvas.SetTop(label, 28); _canvas.Children.Add(label);
        }
    }
}
