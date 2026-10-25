using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.Controls;

public sealed class ToolButtonContent : UserControl
{
    private readonly AnnotationToolIcon _icon = new() { Width = 19, Height = 19 };
    private readonly TextBlock _key = new() { FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.85 };
    private readonly TextBlock _arrow = new() { Text = "▸", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    public ToolButtonContent()
    {
        Width = 30; Height = 30; IsHitTestVisible = false;
        var grid = new Grid(); grid.Children.Add(_icon); grid.Children.Add(_key); grid.Children.Add(_arrow); Content = grid;
        Loaded += (_, _) =>
        {
            IconForeground.InheritFromOwner(this);
            // Initialize labels from the owning button's theme and selection color.
            _key.SetBinding(TextBlock.ForegroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            _arrow.SetBinding(TextBlock.ForegroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        };
    }
    public string Kind { get => _icon.Kind; set => _icon.Kind = value; }
    public string Shortcut { get => _key.Text; set => _key.Text = value; }
    public bool HasDesigns { get => _arrow.Visibility == Visibility.Visible; set => _arrow.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public StrokePattern Pattern { get => _icon.Pattern; set => _icon.Pattern = value; }
}
