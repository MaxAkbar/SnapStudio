using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private Guid? _hoveredObjectId;
    private AnnotationBoundsHandle _hoveredObjectHandle = AnnotationBoundsHandle.Move;

    private bool CanManipulateCanvasObject(global::Windows.System.VirtualKeyModifiers? modifiers = null) =>
        ViewModel.CanManipulateCanvasObjects && (ViewModel.IsMoveToolActive
            || !(modifiers?.HasFlag(global::Windows.System.VirtualKeyModifiers.Control) ?? IsObjectModifierDown(VirtualKey.Control)));

    private void UpdateObjectHover(PointerRoutedEventArgs args)
    {
        AnnotationOverlayItem? item = FindObjectItem(args.OriginalSource as DependencyObject);
        var position = args.GetCurrentPoint(CanvasSurface).Position;
        if (item is not null && !ViewModel.IsAnnotationPointVisible(item.Id, new(position.X / ViewModel.CanvasZoom, position.Y / ViewModel.CanvasZoom))) item = null;
        _hoveredObjectId = item?.Id;
        _hoveredObjectHandle = AnnotationBoundsHandle.Move;
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null && source != CanvasSurface; source = VisualTreeHelper.GetParent(source))
            if (source is FrameworkElement { DataContext: AnnotationOverlayItem, Tag: string name }
                && Enum.TryParse(name, out AnnotationBoundsHandle handle)) { _hoveredObjectHandle = handle; break; }
        RefreshObjectHoverPresentation();
    }

    private void CanvasSurface_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None) return;
        var point = args.GetCurrentPoint(CanvasSurface).Position;
        if (point.X < 0 || point.Y < 0 || point.X > CanvasSurface.ActualWidth || point.Y > CanvasSurface.ActualHeight)
            ClearObjectHover();
    }

    private void ClearObjectHover()
    {
        _hoveredObjectId = null;
        if (ObjectHoverOutline is null) return;
        ObjectHoverOutline.Visibility = Visibility.Collapsed;
        UpdateCanvasPointerCursor();
    }

    private void RefreshObjectHoverPresentation()
    {
        if (ObjectHoverOutline is null) return;
        AnnotationOverlayItem? item = ViewModel.AnnotationOverlays.FirstOrDefault(candidate => candidate.Id == _hoveredObjectId && candidate.IsVisible);
        if (item is null) _hoveredObjectId = null;
        bool visible = item is not null && !item.IsSelected && _canvasDragMode == CanvasDragMode.None && CanManipulateCanvasObject();
        ObjectHoverOutline.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            ObjectHoverOutline.Width = Math.Max(1, item!.Width); ObjectHoverOutline.Height = Math.Max(1, item.Height);
            Canvas.SetLeft(ObjectHoverOutline, item.Left); Canvas.SetTop(ObjectHoverOutline, item.Top);
        }
        UpdateCanvasPointerCursor();
    }

    private void UpdateCanvasPointerCursor()
    {
        AnnotationBoundsHandle? handle = _canvasDragMode is CanvasDragMode.MoveAnnotation or CanvasDragMode.ResizeAnnotation
            ? _annotationDragHandle : _hoveredObjectId.HasValue && CanManipulateCanvasObject() ? _hoveredObjectHandle : null;
        InputSystemCursorShape? cursor = handle switch
        {
            AnnotationBoundsHandle.Move => InputSystemCursorShape.SizeAll,
            AnnotationBoundsHandle.Top or AnnotationBoundsHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
            AnnotationBoundsHandle.Left or AnnotationBoundsHandle.Right => InputSystemCursorShape.SizeWestEast,
            AnnotationBoundsHandle.TopLeft or AnnotationBoundsHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
            AnnotationBoundsHandle.TopRight or AnnotationBoundsHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
            AnnotationBoundsHandle.Start or AnnotationBoundsHandle.End => InputSystemCursorShape.Cross,
            _ => null
        };
        CanvasSurface.UpdateCursor(ViewModel.IsMarqueeToolActive, ViewModel.IsMoveToolActive, cursor);
    }
}
