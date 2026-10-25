using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Rendering;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class SvgThumbnail : UserControl
{
    public static readonly DependencyProperty SvgProperty = DependencyProperty.Register(nameof(Svg), typeof(string), typeof(SvgThumbnail),
        new PropertyMetadata(string.Empty, (sender, _) => ((SvgThumbnail)sender).Render()));
    public static readonly DependencyProperty ThumbnailSizeProperty = DependencyProperty.Register(nameof(ThumbnailSize), typeof(int), typeof(SvgThumbnail),
        new PropertyMetadata(32, (sender, _) => ((SvgThumbnail)sender).Render()));
    public string Svg { get => (string)GetValue(SvgProperty); set => SetValue(SvgProperty, value); }
    public int ThumbnailSize { get => (int)GetValue(ThumbnailSizeProperty); set => SetValue(ThumbnailSizeProperty, value); }

    public SvgThumbnail() => Width = Height = 32;

    public SvgThumbnail(string svg, int size = 32) : this()
    {
        ThumbnailSize = size;
        Svg = svg;
    }

    private void Render()
    {
        int size = Math.Clamp(ThumbnailSize, 8, 256);
        Width = Height = size;
        if (string.IsNullOrWhiteSpace(Svg)) { Content = null; return; }
        try
        {
            var pixels = SvgRasterizer.Preview(Svg, size * 2, size * 2);
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
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Content = new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(this, "SVG preview unavailable: " + error.Message);
        }
    }
}
