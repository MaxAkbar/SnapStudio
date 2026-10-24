using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;

namespace SnapStudio.App.Controls;

public sealed class AnnotationToolIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(AnnotationToolIcon),
        new PropertyMetadata("Select", (sender, _) => ((AnnotationToolIcon)sender).Draw()));

    private readonly Canvas _canvas = new() { Width = 24, Height = 24 };

    public AnnotationToolIcon()
    {
        Width = 24;
        Height = 24;
        IsHitTestVisible = false;
        Loaded += (_, _) => IconForeground.InheritFromOwner(this);
        Content = new Viewbox { Child = _canvas };
        Draw();
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        switch (Kind)
        {
            case "Select":
            case "Rectangle":
                var rectangle = new Rectangle { Width = 18, Height = Kind == "Select" ? 18 : 14, RadiusX = 1, RadiusY = 1 };
                if (Kind == "Select") rectangle.StrokeDashArray = [2, 2];
                Add(rectangle, 3, Kind == "Select" ? 3 : 5);
                break;
            case "Ellipse":
                Add(new Ellipse { Width = 18, Height = 14 }, 3, 5);
                break;
            case "Line":
                Line(new Point(4, 20), new Point(20, 4));
                break;
            case "Arrow":
                Line(new Point(4, 20), new Point(20, 4));
                Line(new Point(9, 4), new Point(20, 4), new Point(20, 15));
                break;
            case "Text":
                Line(new Point(4, 5), new Point(20, 5));
                Line(new Point(12, 5), new Point(12, 20));
                Line(new Point(8, 20), new Point(16, 20));
                break;
            case "Pen":
                Line(new Point(4, 16), new Point(16, 4), new Point(20, 8), new Point(8, 20), new Point(3, 21), new Point(4, 16), new Point(8, 20));
                Line(new Point(13, 7), new Point(17, 11));
                break;
            case "Highlight":
                Line(new Point(5, 15), new Point(14, 3), new Point(21, 8), new Point(12, 20), new Point(5, 15));
                Line(new Point(5, 15), new Point(3, 20), new Point(8, 20));
                Line(new Point(3, 23), new Point(21, 23));
                break;
            case "Fill":
                Line(new Point(9, 3), new Point(18, 12), new Point(9, 21), new Point(1, 13), new Point(10, 4));
                Line(new Point(4, 10), new Point(16, 10));
                Line(new Point(8, 8), new Point(8, 2), new Point(11, 2), new Point(14, 5));
                Line(new Point(21, 13), new Point(19, 18), new Point(21, 20), new Point(23, 18), new Point(21, 13));
                break;
            case "Blur":
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        var pixel = new Rectangle { Width = 4, Height = 4, Opacity = 0.35 + (x + y) * 0.15 };
                        Add(pixel, 3 + x * 7, 3 + y * 7);
                    }
                }
                break;
        }
    }

    private void Line(params Point[] points)
    {
        var collection = new PointCollection();
        foreach (Point point in points) collection.Add(point);
        Add(new Polyline
        {
            Points = collection,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        });
    }

    private void Add(Shape shape, double left = 0, double top = 0)
    {
        shape.StrokeThickness = 1.7;
        shape.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        _canvas.Children.Add(shape);
    }
}
