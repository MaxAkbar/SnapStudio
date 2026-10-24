using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.UI;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private IReadOnlyDictionary<Guid, RectD> _annotationDragBounds = new Dictionary<Guid, RectD>();
    private Guid[] _marqueeOriginalSelection = [];
    private RectD? _marqueeOriginalBounds;
    private RectD? _annotationDragMarqueeBounds;
    private AnnotationSelectionMode _marqueeSelectionMode;

    private void SelectAllObjectsKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || !TryHandleApplicationShortcut(args, ViewModel.HasCurrentDocument)) return;
        ViewModel.UseSelectionTool();
        ViewModel.SelectAllAnnotations();
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private void ShowSelectionMarquee(Point end)
    {
        ShowSelectionMarqueeBounds(CreateDisplayBounds(_annotationDragStart, end));
    }

    private void ShowSelectionMarqueeBounds(RectD bounds)
    {
        foreach (var rectangle in new[] { SelectionMarqueeUnderlay, SelectionMarquee })
        {
            rectangle.Width = bounds.Width;
            rectangle.Height = bounds.Height;
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(rectangle, bounds.X);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(rectangle, bounds.Y);
            rectangle.Visibility = Visibility.Visible;
        }
    }

    private void RefreshPersistentSelectionMarquee()
    {
        if (_canvasDragMode != CanvasDragMode.None) return;
        if (ViewModel.SelectionDisplayBounds is { Width: > 0, Height: > 0 } bounds) ShowSelectionMarqueeBounds(bounds);
        else SelectionMarquee.Visibility = SelectionMarqueeUnderlay.Visibility = Visibility.Collapsed;
    }

    private void ShowMovedSelectionMarquee(IReadOnlyDictionary<Guid, RectD> movedBounds)
    {
        if (_annotationDragMarqueeBounds is not RectD marquee || movedBounds.Count == 0 || _canvasDragMode != CanvasDragMode.MoveAnnotation) return;
        var pair = movedBounds.First();
        RectD original = _annotationDragBounds[pair.Key];
        ShowSelectionMarqueeBounds(new RectD((marquee.X + pair.Value.X - original.X) * ViewModel.CanvasZoom,
            (marquee.Y + pair.Value.Y - original.Y) * ViewModel.CanvasZoom, marquee.Width * ViewModel.CanvasZoom, marquee.Height * ViewModel.CanvasZoom));
    }

    private IReadOnlyDictionary<Guid, RectD> CreateSelectionDragBounds(Point current, bool constrain)
    {
        if (_canvasDragMode == CanvasDragMode.MoveAnnotation && _annotationDragBounds.Count > 1)
        {
            PointD? source = ViewModel.CreateSourcePointFromCanvasPoint(new PointD(current.X, current.Y));
            return source is PointD point ? ViewModel.CreateSelectionMoveBounds(_annotationDragBounds, _annotationDragStartSourcePoint, point) : _annotationDragBounds;
        }
        return CreateAnnotationDragBounds(current, constrain) is RectD bounds ? new Dictionary<Guid, RectD> { [_annotationDragId] = bounds } : new Dictionary<Guid, RectD>();
    }

    private static Microsoft.UI.Xaml.Shapes.Rectangle CreateSelectionOutline(AnnotationOverlayItem item) => new()
    {
        Width = item.Width,
        Height = item.Height,
        Stroke = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255)),
        StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 4, 2 },
        IsHitTestVisible = false
    };
}
