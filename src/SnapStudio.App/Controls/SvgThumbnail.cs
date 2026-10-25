using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Rendering;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class SvgThumbnail : UserControl
{
    public SvgThumbnail(string svg, int size = 32)
    {
        Width = Height = size;
        var pixels = SvgRasterizer.Preview(svg, size * 2, size * 2);
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pixels.PngBytes, writable: false);
        bitmap.SetSource(stream.AsRandomAccessStream());
        Content = new Border
        {
            CornerRadius = new(4),
            Background = new SolidColorBrush(Color.FromArgb(255, 222, 227, 233)),
            Child = new Image { Source = bitmap, Stretch = Stretch.Uniform }
        };
    }
}
