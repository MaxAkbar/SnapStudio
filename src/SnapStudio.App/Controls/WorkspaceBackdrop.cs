using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class WorkspaceBackdrop : Grid
{
    private CanvasVirtualControl? _canvas;
    public static readonly DependencyProperty BackgroundColorProperty = DependencyProperty.Register(
        nameof(BackgroundColor), typeof(Color), typeof(WorkspaceBackdrop),
        new PropertyMetadata(Color.FromArgb(255, 23, 25, 30), PaletteChanged));
    public static readonly DependencyProperty DotColorProperty = DependencyProperty.Register(
        nameof(DotColor), typeof(Color), typeof(WorkspaceBackdrop),
        new PropertyMetadata(Color.FromArgb(255, 40, 44, 52), PaletteChanged));

    public Color BackgroundColor { get => (Color)GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }
    public Color DotColor { get => (Color)GetValue(DotColorProperty); set => SetValue(DotColorProperty, value); }

    private static void PaletteChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((WorkspaceBackdrop)sender)._canvas?.Invalidate();

    public WorkspaceBackdrop()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_canvas is not null) return;
            _canvas = new CanvasVirtualControl();
            _canvas.RegionsInvalidated += Draw;
            _canvas.SizeChanged += (_, _) => _canvas?.Invalidate();
            Children.Add(_canvas);
        };
        Unloaded += (_, _) =>
        {
            _canvas?.RemoveFromVisualTree();
            _canvas = null;
            Children.Clear();
        };
    }

    private void Draw(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var pixels = new Color[14 * 14];
        Array.Fill(pixels, BackgroundColor);
        pixels[0] = DotColor;
        using CanvasBitmap tile = CanvasBitmap.CreateFromColors(sender, pixels, 14, 14);
        using var brush = new CanvasImageBrush(sender, tile)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap
        };
        foreach (var region in args.InvalidatedRegions)
        {
            using CanvasDrawingSession drawing = sender.CreateDrawingSession(region);
            drawing.FillRectangle(region, brush);
        }
    }
}
