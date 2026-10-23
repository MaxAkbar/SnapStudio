using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class TransparencyBackground : Grid
{
    private CanvasVirtualControl? _canvas;

    public TransparencyBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_canvas is not null)
            {
                return;
            }

            _canvas = new CanvasVirtualControl();
            _canvas.RegionsInvalidated += Draw;
            _canvas.SizeChanged += (_, _) => _canvas?.Invalidate();
            Children.Add(_canvas);
        };
        Unloaded += (_, _) =>
        {
            _canvas?.RemoveFromVisualTree();
            Children.Clear();
            _canvas = null;
        };
    }

    private static void Draw(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        Color light = Color.FromArgb(255, 215, 220, 226);
        Color dark = Color.FromArgb(255, 183, 191, 201);
        var pixels = new Color[16 * 16];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                pixels[y * 16 + x] = (x / 8 + y / 8) % 2 == 0 ? light : dark;
            }
        }

        using CanvasBitmap tile = CanvasBitmap.CreateFromColors(sender, pixels, 16, 16);
        using var brush = new CanvasImageBrush(sender, tile)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap
        };
        foreach (Rect region in args.InvalidatedRegions)
        {
            using CanvasDrawingSession drawing = sender.CreateDrawingSession(region);
            drawing.FillRectangle(region, brush);
        }
    }
}
