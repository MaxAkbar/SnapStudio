using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.Core.Rendering;
using Windows.Foundation;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class CanvasGridOverlay : Grid
{
    private CanvasVirtualControl? _canvas;
    public static readonly DependencyProperty CellSizeProperty = DependencyProperty.Register(nameof(CellSize), typeof(double), typeof(CanvasGridOverlay),
        new PropertyMetadata(CanvasGridLayout.DefaultSpacing, (sender, _) => ((CanvasGridOverlay)sender)._canvas?.Invalidate()));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(CanvasGridOverlay),
        new PropertyMetadata(1d, (sender, _) => ((CanvasGridOverlay)sender)._canvas?.Invalidate()));
    public double CellSize { get => (double)GetValue(CellSizeProperty); set => SetValue(CellSizeProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    public CanvasGridOverlay()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_canvas is not null) return;
            _canvas = new CanvasVirtualControl { IsHitTestVisible = false };
            _canvas.RegionsInvalidated += Draw;
            _canvas.SizeChanged += (_, _) => _canvas?.Invalidate();
            Children.Add(_canvas);
        };
        Unloaded += (_, _) =>
        {
            _canvas?.RemoveFromVisualTree();
            Children.Clear(); _canvas = null;
        };
    }

    private void Draw(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        CanvasGridLayout layout = CanvasGridLayout.Create(CellSize, Zoom);
        foreach (Rect region in args.InvalidatedRegions)
        {
            using var drawing = sender.CreateDrawingSession(region);
            drawing.Clear(Color.FromArgb(0, 0, 0, 0));
            foreach (CanvasGridLine line in layout.Lines(region.X - 1, region.Right + 1))
            {
                float x = (float)line.Position;
                drawing.DrawLine(x, (float)region.Y, x, (float)region.Bottom, Color.FromArgb(line.IsMajor ? (byte)120 : (byte)75, 255, 255, 255), 1.5f);
                drawing.DrawLine(x, (float)region.Y, x, (float)region.Bottom, Color.FromArgb(line.IsMajor ? (byte)145 : (byte)90, 30, 65, 95), 0.75f);
            }
            foreach (CanvasGridLine line in layout.Lines(region.Y - 1, region.Bottom + 1))
            {
                float y = (float)line.Position;
                drawing.DrawLine((float)region.X, y, (float)region.Right, y, Color.FromArgb(line.IsMajor ? (byte)120 : (byte)75, 255, 255, 255), 1.5f);
                drawing.DrawLine((float)region.X, y, (float)region.Right, y, Color.FromArgb(line.IsMajor ? (byte)145 : (byte)90, 30, 65, 95), 0.75f);
            }
        }
    }
}
