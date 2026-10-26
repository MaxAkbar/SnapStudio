using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Geometry;
using System.Numerics;
using SnapStudio.Core.Primitives;
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
    public static readonly DependencyProperty PerspectiveProperty = DependencyProperty.Register(nameof(Perspective), typeof(PerspectiveGridSettings), typeof(CanvasGridOverlay),
        new PropertyMetadata(null, (sender, _) => ((CanvasGridOverlay)sender)._canvas?.Invalidate()));
    public static readonly DependencyProperty FamiliesProperty = DependencyProperty.Register(nameof(Families), typeof(CanvasGridFamilies), typeof(CanvasGridOverlay),
        new PropertyMetadata(null, (sender, _) => ((CanvasGridOverlay)sender)._canvas?.Invalidate()));
    public CanvasGridFamilies? Families { get => (CanvasGridFamilies?)GetValue(FamiliesProperty); set => SetValue(FamiliesProperty, value); }
    public PerspectiveGridSettings? Perspective { get => (PerspectiveGridSettings?)GetValue(PerspectiveProperty); set => SetValue(PerspectiveProperty, value); }
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
        var families = (Families ?? new()).Normalize();
        static Color NativeColor(ColorRgba color) => Color.FromArgb(color.A, color.R, color.G, color.B);
        var gridStyle = families.Get(GridGuideFamily.Grid);
        Color lineColor = NativeColor(gridStyle.GetColor()), lineContrast = NativeColor(gridStyle.GetContrastColor());
        var perspective = Perspective is { Mode: not CanvasGridMode.Rectangular } settings && Zoom > 0
            ? PerspectiveGrid.Create(new RectD(0, 0, ActualWidth / Zoom, ActualHeight / Zoom), settings, families) : null;
        foreach (Rect region in args.InvalidatedRegions)
        {
            using var drawing = sender.CreateDrawingSession(region);
            drawing.Clear(Color.FromArgb(0, 0, 0, 0));
            if (perspective is not null)
            {
                foreach (var stroke in perspective)
                {
                    var style = families.Get(stroke.Family);
                    using var builder = new CanvasPathBuilder(sender);
                    builder.BeginFigure(new Vector2((float)(stroke.Points[0].X * Zoom), (float)(stroke.Points[0].Y * Zoom)));
                    foreach (var point in stroke.Points.Skip(1)) builder.AddLine(new Vector2((float)(point.X * Zoom), (float)(point.Y * Zoom)));
                    builder.EndFigure(CanvasFigureLoop.Open);
                    using var path = CanvasGeometry.CreatePath(builder);
                    drawing.DrawGeometry(path, NativeColor(style.GetContrastColor()), 2f);
                    drawing.DrawGeometry(path, NativeColor(style.GetColor()), stroke.IsMajor ? 1.15f : .8f);
                }
                continue;
            }
            foreach (CanvasGridLine line in layout.Lines(region.X - 1, region.Right + 1))
            {
                float x = (float)line.Position;
                drawing.DrawLine(x, (float)region.Y, x, (float)region.Bottom, lineContrast, 2f);
                drawing.DrawLine(x, (float)region.Y, x, (float)region.Bottom, lineColor, line.IsMajor ? 1.15f : .8f);
            }
            foreach (CanvasGridLine line in layout.Lines(region.Y - 1, region.Bottom + 1))
            {
                float y = (float)line.Position;
                drawing.DrawLine((float)region.X, y, (float)region.Right, y, lineContrast, 2f);
                drawing.DrawLine((float)region.X, y, (float)region.Right, y, lineColor, line.IsMajor ? 1.15f : .8f);
            }
        }
    }
}
