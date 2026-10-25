using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.UI;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private IReadOnlyList<RectD> _objectSnapTargets = [];
    private IReadOnlyList<AnnotationSnapGuide> _objectSnapGuides = [];
    private DocumentId? _objectSnapDocumentId;
    private AnnotationKind? _toolDrawingKind;
    private PerspectiveLineFamily? _drawingLineFamily;
    private PerspectiveCurveDrawingResult? _perspectiveLineDrawing;
    private bool _toolDrawingPerspectiveLine;
    private void ResetPerspectiveGrid_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) => ViewModel.ResetPerspectiveGrid();

    private void ShowObjectSnapGuides()
    {
        ObjectSnapGuides.Children.Clear();
        double zoom = ViewModel.CanvasZoom;
        foreach (AnnotationSnapGuide guide in _objectSnapGuides)
        {
            if (guide.Path is { Count: > 1 } points)
            {
                var path = new Microsoft.UI.Xaml.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 213, 63, 181)), StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false
                };
                foreach (var point in points) path.Points.Add(new(point.X * zoom, point.Y * zoom));
                AutomationProperties.SetName(path, "Perspective alignment guide"); ObjectSnapGuides.Children.Add(path); continue;
            }
            bool vertical = guide.Axis == SnapGuideAxis.Vertical;
            double extent = vertical ? ViewModel.CanvasDisplayHeight : ViewModel.CanvasDisplayWidth;
            double from = Math.Max(0, guide.From * zoom - 8), to = Math.Min(extent, guide.To * zoom + 8);
            var line = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = vertical ? guide.Position * zoom : from,
                Y1 = vertical ? from : guide.Position * zoom,
                X2 = vertical ? guide.Position * zoom : to,
                Y2 = vertical ? to : guide.Position * zoom,
                Stroke = new SolidColorBrush(Color.FromArgb(255, 213, 63, 181)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            AutomationProperties.SetName(line, $"{(vertical ? "Vertical" : "Horizontal")} {(guide.IsGrid ? "grid " : guide.IsCenter ? "center " : "")}alignment guide");
            ObjectSnapGuides.Children.Add(line);
        }
    }
}
