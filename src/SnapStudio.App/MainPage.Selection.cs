using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.UI;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private IReadOnlyDictionary<Guid, RectD> _annotationDragBounds = new Dictionary<Guid, RectD>();
    private Guid[] _marqueeOriginalSelection = [];
    private RectD? _marqueeOriginalBounds;
    private RectD? _annotationDragMarqueeBounds;
    private AnnotationSelectionMode _marqueeSelectionMode;
    private Point _marqueeLastPointer;
    private RectD? _marqueePreviewBounds;
    private Storyboard? _marqueeAnimation;
    private bool _marqueeAnimationRunning;

    private void SelectAllObjectsKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || !TryHandleApplicationShortcut(args, ViewModel.HasCurrentDocument)) return;
        if (ViewModel.IsMarqueeToolActive) ViewModel.SelectEntireCanvas();
        else { ViewModel.UseMoveTool(); ViewModel.SelectAllAnnotations(); }
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private void ShowSelectionMarquee(Point end, VirtualKeyModifiers modifiers)
    {
        var canvas = new SizeD(ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight);
        if (IsObjectModifierDown(VirtualKey.Space) && _marqueePreviewBounds is RectD previous)
        {
            RectD moved = MarqueeBoundsEditor.Move(previous, end.X - _marqueeLastPointer.X, end.Y - _marqueeLastPointer.Y, canvas);
            _annotationDragStart = new(_annotationDragStart.X + moved.X - previous.X, _annotationDragStart.Y + moved.Y - previous.Y);
            _marqueePreviewBounds = moved;
        }
        else _marqueePreviewBounds = MarqueeBoundsEditor.Create(new(_annotationDragStart.X, _annotationDragStart.Y), new(end.X, end.Y), canvas,
            square: ViewModel.IsMarqueeToolActive && _marqueeSelectionMode == AnnotationSelectionMode.Replace && modifiers.HasFlag(VirtualKeyModifiers.Shift),
            fromCenter: ViewModel.IsMarqueeToolActive && modifiers.HasFlag(VirtualKeyModifiers.Menu));
        _marqueeLastPointer = end;
        ShowSelectionMarqueeBounds(_marqueePreviewBounds.Value);
    }

    private void ShowMovingMarquee(Point end)
    {
        if (_marqueeOriginalBounds is not RectD source) return;
        RectD original = new(source.X * ViewModel.CanvasZoom, source.Y * ViewModel.CanvasZoom, source.Width * ViewModel.CanvasZoom, source.Height * ViewModel.CanvasZoom);
        ShowSelectionMarqueeBounds(MarqueeBoundsEditor.Move(original, end.X - _annotationDragStart.X, end.Y - _annotationDragStart.Y,
            new(ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight)));
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
        if (!_marqueeAnimationRunning && IsLoaded)
        {
            if (_marqueeAnimation is null)
            {
                var offset = new DoubleAnimation { From = 0, To = -8, Duration = TimeSpan.FromMilliseconds(650), EnableDependentAnimation = true, RepeatBehavior = RepeatBehavior.Forever };
                Storyboard.SetTarget(offset, SelectionMarquee);
                Storyboard.SetTargetProperty(offset, "StrokeDashOffset");
                _marqueeAnimation = new Storyboard();
                _marqueeAnimation.Children.Add(offset);
            }
            _marqueeAnimation.Begin(); _marqueeAnimationRunning = true;
        }
    }

    private void StopMarqueeAnimation()
    {
        _marqueeAnimation?.Stop(); _marqueeAnimationRunning = false;
    }

    private void RefreshPersistentSelectionMarquee()
    {
        if (_canvasDragMode != CanvasDragMode.None) return;
        if (ViewModel.SelectionDisplayBounds is { Width: > 0, Height: > 0 } bounds) ShowSelectionMarqueeBounds(bounds);
        else { StopMarqueeAnimation(); SelectionMarquee.Visibility = SelectionMarqueeUnderlay.Visibility = Visibility.Collapsed; }
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
        if (_canvasDragMode == CanvasDragMode.MoveAnnotation && _annotationDragBounds.Count > 0)
        {
            PointD? source = ViewModel.CreateSourcePointFromCanvasPoint(new PointD(current.X, current.Y));
            return source is PointD point ? ViewModel.CreateSelectionMoveBounds(_annotationDragBounds, _annotationDragStartSourcePoint, point, constrain) : _annotationDragBounds;
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
