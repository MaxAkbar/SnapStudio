using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using IconPath = Microsoft.UI.Xaml.Shapes.Path;

namespace SnapStudio.App.Controls;

public sealed class WorkspaceIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(WorkspaceIcon),
        new PropertyMetadata("Image", (sender, _) => ((WorkspaceIcon)sender).Draw()));

    private readonly Canvas _canvas = new() { Width = 24, Height = 24 };

    public WorkspaceIcon()
    {
        Width = 24;
        Height = 24;
        IsHitTestVisible = false;
        Loaded += (_, _) => IconForeground.InheritFromOwner(this);
        Content = new Viewbox { Child = _canvas, Stretch = Stretch.Uniform };
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
            case "Grid0": Path("M3 3H21V21H3Z M9 3V21 M15 3V21 M3 9H21 M3 15H21"); break;
            case "Grid1": Path("M3 5H21 M12 8L3 21 M12 8L21 21 M12 8V21 M5 16H19"); Circle(12, 8, 1.5); break;
            case "Grid2": Path("M2 8H22 M2 8L12 21L22 8 M2 8L12 15L22 8 M12 15V21"); Circle(2, 8, 1); Circle(22, 8, 1); break;
            case "Grid3": Path("M2 7L12 22L22 7Z M12 2L8 15 M12 2L16 15 M2 7L16 15 M22 7L8 15"); Circle(12, 2, 1); break;
            case "Grid4": Path("M12 2C2 7 2 17 12 22 M12 2C22 7 22 17 12 22 M2 10L20 17 M22 10L4 17 M12 2V22"); break;
            case "Grid5": Circle(12, 12, 10); Path("M12 2C3 7 3 17 12 22 M12 2C21 7 21 17 12 22 M2 12C7 3 17 3 22 12 M2 12C7 21 17 21 22 12 M2 12H22 M12 2V22"); break;
            case "PerspectiveLine": Path("M5 21C20 17 20 7 5 3 M3 3H7 M3 21H7"); break;
            case "PerspectiveRectangle": Path("M3 6L20 3V20L3 16Z"); break;
            case "Library":
                Rectangle(3, 4, 18, 16, 2);
                Path("M8 4V20 M12 9H17 M12 14H17");
                break;
            case "PanelLeft":
                Rectangle(3, 4, 18, 16, 2);
                Path("M9 4V20");
                break;
            case "PanelRight":
                Rectangle(3, 4, 18, 16, 2);
                Path("M15 4V20");
                break;
            case "Camera":
                Path("M8 5L6 8H4A2 2 0 0 0 2 10V19A2 2 0 0 0 4 21H20A2 2 0 0 0 22 19V10A2 2 0 0 0 20 8H18L16 5Z");
                Circle(12, 14, 4);
                break;
            case "Folder":
                Path("M3 7A2 2 0 0 1 5 5H10L12 8H19A2 2 0 0 1 21 10V18A2 2 0 0 1 19 20H5A2 2 0 0 1 3 18Z");
                break;
            case "Plus":
                Path("M12 5V19 M5 12H19");
                break;
            case "Minus":
                Path("M5 12H19");
                break;
            case "Paste":
                Path("M8 5H5A2 2 0 0 0 3 7V20A2 2 0 0 0 5 22H19A2 2 0 0 0 21 20V7A2 2 0 0 0 19 5H16");
                Rectangle(8, 2, 8, 6, 2);
                break;
            case "Copy":
                Rectangle(8, 8, 13, 13, 2);
                Path("M16 8V5A2 2 0 0 0 14 3H5A2 2 0 0 0 3 5V14A2 2 0 0 0 5 16H8");
                break;
            case "Export":
                Path("M12 15V3 M7 8L12 3L17 8 M4 14V19A2 2 0 0 0 6 21H18A2 2 0 0 0 20 19V14");
                break;
            case "Focus":
                Path("M8 3H3V8 M16 3H21V8 M3 16V21H8 M21 16V21H16");
                break;
            case "Undo":
                Path("M8 4L3 9L8 14 M3 9H13A7 7 0 0 1 13 23");
                break;
            case "Redo":
                Path("M16 4L21 9L16 14 M21 9H11A7 7 0 0 0 11 23");
                break;
            case "ChevronLeft":
                Path("M15 5L8 12L15 19");
                break;
            case "ChevronRight":
                Path("M9 5L16 12L9 19");
                break;
            case "ChevronDown":
                Path("M7 10L12 15L17 10");
                break;
            case "ChevronUp":
                Path("M7 14L12 9L17 14");
                break;
            case "More":
                Circle(5, 12, 1);
                Circle(12, 12, 1);
                Circle(19, 12, 1);
                break;
            case "Settings":
                Path("M10 2L9 5L6 6L3 5L1 9L3 11V14L1 16L3 20L6 19L9 20L10 23H14L15 20L18 19L21 20L23 16L21 14V11L23 9L21 5L18 6L15 5L14 2Z");
                Circle(12, 12, 3);
                break;
            case "Image":
                Rectangle(3, 3, 18, 18, 3);
                Circle(8, 8, 1.5);
                Path("M3 17L8 12L12 16L16 9L21 16");
                break;
            case "Layers":
                Path("M2 8L12 3L22 8L12 13Z M2 13L12 18L22 13 M2 18L12 23L22 18");
                break;
            case "Eye":
                Path("M2 12C2 12 6 5 12 5C18 5 22 12 22 12C22 12 18 19 12 19C6 19 2 12 2 12Z");
                Circle(12, 12, 3);
                break;
            case "Lock":
                Rectangle(5, 10, 14, 11, 2);
                Path("M8 10V6A4 4 0 0 1 16 6V10");
                break;
            case "Crop":
                Path("M6 2V18H22 M2 6H18V22");
                break;
            case "Check":
                Path("M5 12L9 16L19 6");
                break;
            case "Arrow":
                Path("M4 20L20 4 M9 4H20V15");
                break;
            case "Move":
                Path("M5 3L19 13L12 14L9 21Z");
                break;
            case "Link":
                Path("M10 8L13 5A4 4 0 0 1 19 11L16 14 M14 16L11 19A4 4 0 0 1 5 13L8 10 M9 15L15 9");
                break;
            case "Refresh":
                Path("M20 10A8 8 0 0 0 6 6L3 9 M3 3V9H9 M4 14A8 8 0 0 0 18 18L21 15 M21 21V15H15");
                break;
            case "Search":
                Circle(10.5, 10.5, 6.5);
                Path("M16 16L20 20");
                break;
            case "Trash":
                Path("M3 6H21 M9 6V3H15V6 M5 6L6 21H18L19 6 M10 10V17 M14 10V17");
                break;
            case "Fit":
                Path("M8 3H3V8 M16 3H21V8 M3 16V21H8 M21 16V21H16");
                Rectangle(7, 7, 10, 10, 1);
                break;
        }
    }

    private void Path(string data)
    {
        // Only the fixed vector paths above are parsed; no external markup is loaded.
        var path = (IconPath)XamlReader.Load(
            $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{data}\" />");
        Add(path);
    }

    private void Rectangle(double left, double top, double width, double height, double radius)
    {
        Add(new Rectangle { Width = width, Height = height, RadiusX = radius, RadiusY = radius }, left, top);
    }

    private void Circle(double centerX, double centerY, double radius)
    {
        Add(new Ellipse { Width = radius * 2, Height = radius * 2 }, centerX - radius, centerY - radius);
    }

    private void Add(Shape shape, double left = 0, double top = 0)
    {
        shape.StrokeThickness = 1.65;
        shape.StrokeStartLineCap = PenLineCap.Round;
        shape.StrokeEndLineCap = PenLineCap.Round;
        shape.StrokeLineJoin = PenLineJoin.Round;
        shape.SetBinding(Shape.StrokeProperty, new Binding
        {
            Source = this,
            Path = new PropertyPath(nameof(Foreground))
        });
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        _canvas.Children.Add(shape);
    }
}
