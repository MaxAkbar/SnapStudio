using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class WorkspaceBackdrop : Grid
{
    private CanvasVirtualControl? _canvas;

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

    private static void Draw(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var pixels = new Color[14 * 14];
        Array.Fill(pixels, Color.FromArgb(255, 23, 25, 30));
        pixels[0] = Color.FromArgb(255, 40, 44, 52);
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
