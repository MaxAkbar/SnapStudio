using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SnapStudio.App.ViewModels.Items;
using Windows.Foundation;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private readonly SemaphoreSlim _objectActionGate = new(1, 1);
    private MenuFlyout? _objectContextMenu;

    private enum ObjectAction { Duplicate, ToggleVisibility, Delete, Forward, Backward, Front, Back }

    private async void ObjectActionKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || !TryHandleApplicationShortcut(args, ViewModel.HasSelectedAnnotation)) return;
        ObjectAction action = sender.Key switch
        {
            VirtualKey.D => ObjectAction.Duplicate,
            VirtualKey.H => ObjectAction.ToggleVisibility,
            VirtualKey.Delete => ObjectAction.Delete,
            VirtualKey.PageUp => sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift) ? ObjectAction.Front : ObjectAction.Forward,
            _ => sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift) ? ObjectAction.Back : ObjectAction.Backward
        };
        await RunObjectActionAsync(action);
    }

    private void ClearObjectSelectionKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Escape first dismisses an open menu, retaining its selected object.
        if (_objectContextMenu is not null || _canvasDragMode != CanvasDragMode.None
            || !TryHandleApplicationShortcut(args, ViewModel.HasSelectedAnnotation)) return;
        ViewModel.ClearAnnotationSelection();
        UpdateBindingsFromViewModel();
    }

    private async void CanvasScrollViewer_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_canvasDragMode != CanvasDragMode.None || !ViewModel.HasSelectedAnnotation || ShouldIgnoreApplicationShortcut()
            || IsObjectModifierDown(VirtualKey.Menu)) return;
        if (IsObjectModifierDown(VirtualKey.Control))
        {
            // ScrollViewer consumes these keys before page accelerators run.
            if (e.Key is VirtualKey.PageUp or VirtualKey.PageDown)
            {
                e.Handled = true;
                bool front = e.Key == VirtualKey.PageUp;
                await RunObjectActionAsync(IsObjectModifierDown(VirtualKey.Shift)
                    ? (front ? ObjectAction.Front : ObjectAction.Back)
                    : (front ? ObjectAction.Forward : ObjectAction.Backward));
            }
            return;
        }
        (int x, int y) = e.Key switch
        {
            VirtualKey.Left => (-1, 0),
            VirtualKey.Right => (1, 0),
            VirtualKey.Up => (0, -1),
            VirtualKey.Down => (0, 1),
            _ => (0, 0)
        };
        if (x == 0 && y == 0) return;
        e.Handled = true;
        int step = IsObjectModifierDown(VirtualKey.Shift) ? 10 : 1;
        await RunSelectedObjectOperationAsync(token => ViewModel.NudgeSelectedAnnotationAsync(x * step, y * step, token));
    }

    private static bool IsObjectModifierDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void CanvasObject_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || !ViewModel.HasCurrentDocument) return;
        bool pointer = args.TryGetPosition(this, out Point position);
        AnnotationOverlayItem? item = FindObjectItem(args.OriginalSource as DependencyObject);
        if (item is null && !pointer) item = GetSelectedObjectItem();
        if (item is null) return;
        if (!pointer)
        {
            Point center = CanvasSurface.TransformToVisual(CanvasScrollViewer).TransformPoint(new Point(item.Left + item.Width / 2, item.Top + item.Height / 2));
            center = new Point(Math.Clamp(center.X, 0, CanvasScrollViewer.ActualWidth), Math.Clamp(center.Y, 0, CanvasScrollViewer.ActualHeight));
            position = CanvasScrollViewer.TransformToVisual(this).TransformPoint(center);
        }
        args.Handled = true;
        ShowObjectContextMenu(item, position);
    }

    private void AnnotationObject_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: AnnotationOverlayItem item }) return;
        args.Handled = true;
        Point position = args.TryGetPosition(this, out Point pointer) ? pointer
            : ((FrameworkElement)sender).TransformToVisual(this).TransformPoint(new Point(20, 18));
        ShowObjectContextMenu(item, position);
    }

    private static AnnotationOverlayItem? FindObjectItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: AnnotationOverlayItem item }) return item;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private AnnotationOverlayItem? GetSelectedObjectItem() => ViewModel.AnnotationLayers
        .SelectMany(layer => layer.Objects).FirstOrDefault(item => item.Id == ViewModel.SelectedAnnotationId);

    private void ShowObjectContextMenu(AnnotationOverlayItem item, Point position)
    {
        CanvasScrollViewer.Focus(FocusState.Programmatic);
        ViewModel.UseSelectionTool();
        ViewModel.SelectAnnotation(item.Id);
        item = GetSelectedObjectItem() ?? item;
        _objectContextMenu?.Hide();
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = item.LayerLabel, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateObjectMenuAction("Duplicate", "Ctrl+D", ObjectAction.Duplicate));
        menu.Items.Add(CreateObjectMenuAction(item.IsVisible ? "Hide object" : "Show object", "Ctrl+H", ObjectAction.ToggleVisibility));
        var arrange = new MenuFlyoutSubItem { Text = "Arrange within layer" };
        arrange.Items.Add(CreateObjectMenuAction("Bring forward", "Ctrl+PgUp", ObjectAction.Forward, item.CanMoveUp));
        arrange.Items.Add(CreateObjectMenuAction("Send backward", "Ctrl+PgDn", ObjectAction.Backward, item.CanMoveDown));
        arrange.Items.Add(CreateObjectMenuAction("Bring to front", "Ctrl+Shift+PgUp", ObjectAction.Front, item.CanMoveUp));
        arrange.Items.Add(CreateObjectMenuAction("Send to back", "Ctrl+Shift+PgDn", ObjectAction.Back, item.CanMoveDown));
        menu.Items.Add(arrange);
        var layers = new MenuFlyoutSubItem { Text = "Move to layer" };
        foreach (AnnotationLayerItem layer in ViewModel.AnnotationLayers)
        {
            var move = new MenuFlyoutItem { Text = layer.Name, IsEnabled = layer.CanMoveSelectedHere };
            Guid id = layer.Id;
            move.Click += async (_, _) => await RunSelectedObjectOperationAsync(token => ViewModel.MoveSelectedAnnotationToLayerAsync(id, token));
            layers.Items.Add(move);
        }
        layers.IsEnabled = ViewModel.AnnotationLayers.Any(layer => layer.CanMoveSelectedHere);
        menu.Items.Add(layers);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateObjectMenuAction("Delete", "Delete", ObjectAction.Delete));
        menu.Closed += (_, _) => { if (ReferenceEquals(_objectContextMenu, menu)) _objectContextMenu = null; };
        _objectContextMenu = menu;
        menu.ShowAt(this, new FlyoutShowOptions { Position = position });
    }

    private MenuFlyoutItem CreateObjectMenuAction(string text, string shortcut, ObjectAction action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, KeyboardAcceleratorTextOverride = shortcut, IsEnabled = enabled };
        item.Click += async (_, _) => await RunObjectActionAsync(action);
        return item;
    }

    private Task RunObjectActionAsync(ObjectAction action) => RunSelectedObjectOperationAsync(token => action switch
    {
        ObjectAction.Duplicate => ViewModel.DuplicateSelectedAnnotationAsync(token),
        ObjectAction.ToggleVisibility => ViewModel.SelectedAnnotationId is Guid id ? ViewModel.ToggleAnnotationVisibilityAsync(id, token) : Task.CompletedTask,
        ObjectAction.Delete => ViewModel.DeleteSelectedAnnotationAsync(token),
        ObjectAction.Forward => ViewModel.SelectedAnnotationId is Guid id ? ViewModel.MoveAnnotationInLayerAsync(id, 1, token) : Task.CompletedTask,
        ObjectAction.Backward => ViewModel.SelectedAnnotationId is Guid id ? ViewModel.MoveAnnotationInLayerAsync(id, -1, token) : Task.CompletedTask,
        ObjectAction.Front => ViewModel.ArrangeSelectedAnnotationAsync(true, token),
        _ => ViewModel.ArrangeSelectedAnnotationAsync(false, token)
    });

    private async Task RunSelectedObjectOperationAsync(Func<CancellationToken, Task> operation)
    {
        var documentId = ViewModel.CurrentDocumentId;
        Guid? objectId = ViewModel.SelectedAnnotationId;
        if (objectId is null) return;
        _objectContextMenu?.Hide();
        await _objectActionGate.WaitAsync();
        try
        {
            // Queued arrow repeats must never act on a different selection or document.
            if (ViewModel.CurrentDocumentId != documentId || ViewModel.SelectedAnnotationId != objectId || ShouldIgnoreApplicationShortcut()) return;
            await operation(CancellationToken.None);
            UpdateBindingsFromViewModel();
            if (!ShouldIgnoreApplicationShortcut()) CanvasScrollViewer.Focus(FocusState.Programmatic);
        }
        finally { _objectActionGate.Release(); }
    }
}
