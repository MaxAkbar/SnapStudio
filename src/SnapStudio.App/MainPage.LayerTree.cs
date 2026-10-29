using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using Windows.Foundation;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private readonly SemaphoreSlim _layerTreeActionGate = new(1, 1);
    private void LayerTreeActions_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Ctrl-click rows to select several", IsEnabled = false });
        AddStructureMenuActions(menu);
        if (ViewModel.LayerTreeRows.FirstOrDefault(row => row.IsSelected && row.Kind is LayerTreeRowKind.Folder or LayerTreeRowKind.ObjectGroup) is { } group)
            AddLayerAction(menu, "Ungroup selected group", () => ViewModel.UngroupTreeRowAsync(group, CancellationToken.None));
        menu.Items.Add(CreateMoveObjectsMenu());
        _objectContextMenu?.Hide(); _objectContextMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_objectContextMenu, menu)) _objectContextMenu = null; };
        menu.ShowAt(element);
    }
    private void LayerTreeSelect_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: LayerTreeRow row }) return;
        ViewModel.SelectTreeRow(row, IsObjectModifierDown(VirtualKey.Control), IsObjectModifierDown(VirtualKey.Shift)); UpdateBindingsFromViewModel();
        WorkspaceLayerList.Focus(FocusState.Programmatic);
    }
    private void LayerTreeExpand_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: LayerTreeRow row }) ViewModel.SetTreeExpanded(row);
    }
    private async Task RunLayerTreeActionAsync(Func<Task> action)
    {
        _objectContextMenu?.Hide();
        if (!await _layerTreeActionGate.WaitAsync(0)) return;
        try { await action(); UpdateBindingsFromViewModel(); }
        catch (Exception error) { ViewModel.ShowCaptureHistoryError("Layer operation failed: " + error.Message); }
        finally { _layerTreeActionGate.Release(); }
    }
    private async void LayerTreeVisibility_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: LayerTreeRow row }) return;
        await RunLayerTreeActionAsync(() => row.Kind == LayerTreeRowKind.Object ? ViewModel.ToggleAnnotationVisibilityAsync(row.Id, CancellationToken.None)
            : row.Kind == LayerTreeRowKind.ObjectGroup ? ViewModel.ToggleObjectGroupAsync(row.Id, CancellationToken.None) : ViewModel.ToggleLayerVisibilityAsync(row.Id, CancellationToken.None));
    }
    private void LayerTreeRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: LayerTreeRow row } element || _canvasDragMode != CanvasDragMode.None) return;
        args.Handled = true; ViewModel.EnsureTreeContextSelection(row);
        Point position = args.TryGetPosition(this, out var p) ? p : element.TransformToVisual(this).TransformPoint(new Point(24, 20));
        if (row.Object is { } annotation) { ShowObjectContextMenu(annotation, position); return; }
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = row.Name, IsEnabled = false });
        AddStructureMenuActions(menu);
        if (row.Kind == LayerTreeRowKind.ObjectGroup)
        {
            AddLayerAction(menu, "Ungroup objects", () => ViewModel.UngroupTreeRowAsync(row, CancellationToken.None));
            AddLayerAction(menu, "Rename object group…", () => RenameObjectGroupAsync(row));
            AddLayerAction(menu, "Cut objects", () => { ViewModel.CutTreeObjects(); return Task.CompletedTask; });
            menu.Items.Add(CreateMoveObjectsMenu());
            AddLayerAction(menu, "Duplicate objects", () => ViewModel.DuplicateSelectedAnnotationAsync(CancellationToken.None), ViewModel.HasSelectedAnnotation);
            AddLayerAction(menu, "Bring forward", () => ViewModel.ArrangeSelectionAsync(1, false, CancellationToken.None), ViewModel.HasSelectedAnnotation);
            AddLayerAction(menu, "Send backward", () => ViewModel.ArrangeSelectionAsync(-1, false, CancellationToken.None), ViewModel.HasSelectedAnnotation);
            AddLayerAction(menu, "Delete object group", () => ViewModel.DeleteObjectGroupAsync(row.Id, CancellationToken.None));
        }
        if (row.Layer is { } layer)
        {
            var rename = new MenuFlyoutItem { Text = row.Kind == LayerTreeRowKind.Folder ? "Rename folder…" : "Rename layer…", DataContext = layer };
            rename.Click += RenameLayerMenuItem_Click; menu.Items.Add(rename);
            if (layer.IsFolder)
            {
                AddLayerAction(menu, "New layer in folder", () => ViewModel.AddTreeLayerAsync(layer.Id, CancellationToken.None));
                AddLayerAction(menu, "Ungroup folder", () => ViewModel.UngroupTreeRowAsync(row, CancellationToken.None));
            }
            else
            {
                AddLayerAction(menu, "Paste objects here", () => ViewModel.PasteTreeObjectsAsync(layer.Id, CancellationToken.None), ViewModel.CanPasteCutObjects);
                var folders = new MenuFlyoutSubItem { Text = "Move selected layers to folder" };
                var top = new MenuFlyoutItem { Text = "Outside folders" }; top.Click += async (_, _) => await RunLayerTreeActionAsync(() => ViewModel.MoveTreeLayersToFolderAsync(null, CancellationToken.None)); folders.Items.Add(top);
                foreach (var folder in ViewModel.AnnotationLayers.Where(item => item.IsFolder))
                {
                    var target = new MenuFlyoutItem { Text = folder.Name }; Guid id = folder.Id;
                    target.Click += async (_, _) => await RunLayerTreeActionAsync(() => ViewModel.MoveTreeLayersToFolderAsync(id, CancellationToken.None)); folders.Items.Add(target);
                }
                menu.Items.Add(folders);
                foreach (var (text, tag, enabled) in new[] { (layer.MaskToggleText, "ToggleMask", layer.HasLayerMask), ("Remove layer mask", "RemoveMask", layer.HasLayerMask), ("Reset layer crop", "ResetCrop", layer.HasLayerCrop) })
                { var item = new MenuFlyoutItem { Text = text, Tag = tag, DataContext = layer, IsEnabled = enabled }; item.Click += LayerSelectionAction_Click; menu.Items.Add(item); }
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            AddLayerAction(menu, "Move up", () => ViewModel.MoveTreeLayerAsync(layer.Id, 1, CancellationToken.None), layer.CanMoveUp);
            AddLayerAction(menu, "Move down", () => ViewModel.MoveTreeLayerAsync(layer.Id, -1, CancellationToken.None), layer.CanMoveDown);
            var colors = new MenuFlyoutSubItem { Text = layer.ColorMenuText };
            foreach (var color in Enum.GetValues<LayerColorLabel>()) { var item = new MenuFlyoutItem { Text = color.ToString(), Tag = color.ToString(), DataContext = layer }; item.Click += LayerColorMenuItem_Click; colors.Items.Add(item); }
            var custom = new MenuFlyoutItem { Text = "Custom…", DataContext = layer }; custom.Click += LayerCustomColorMenuItem_Click; colors.Items.Add(custom); menu.Items.Add(colors);
            var delete = new MenuFlyoutItem { Text = layer.IsFolder ? "Delete folder and layers…" : "Delete layer…", DataContext = layer, IsEnabled = layer.CanDelete }; delete.Click += DeleteLayer_Click; menu.Items.Add(delete);
        }
        _objectContextMenu?.Hide(); _objectContextMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_objectContextMenu, menu)) _objectContextMenu = null; };
        menu.ShowAt(this, new FlyoutShowOptions { Position = position });
    }
    private void AddLayerAction(MenuFlyout menu, string label, Func<Task> action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled }; item.Click += async (_, _) => await RunLayerTreeActionAsync(action); menu.Items.Add(item);
    }
    private void AddStructureMenuActions(MenuFlyout menu)
    {
        menu.Items.Add(new MenuFlyoutSeparator());
        AddLayerAction(menu, "Group selected layers", () => ViewModel.GroupTreeSelectionAsync(CancellationToken.None), ViewModel.CanGroupLayers);
        AddLayerAction(menu, "Group selected objects", () => ViewModel.GroupTreeSelectionAsync(CancellationToken.None), ViewModel.CanGroupObjects);
        AddLayerAction(menu, "Merge selection (keep editable)", () => ViewModel.MergeTreeSelectionAsync(CancellationToken.None), ViewModel.CanMergeLayerTree);
        menu.Items.Add(new MenuFlyoutSeparator());
    }
    private MenuFlyoutSubItem CreateMoveObjectsMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = "Move objects to layer", IsEnabled = ViewModel.HasSelectedAnnotation };
        foreach (var layer in ViewModel.ObjectDestinationLayers)
        {
            var item = new MenuFlyoutItem { Text = layer.Name }; Guid id = layer.Id;
            item.Click += async (_, _) => await RunLayerTreeActionAsync(() => ViewModel.MoveTreeObjectsAsync(id, CancellationToken.None)); menu.Items.Add(item);
        }
        return menu;
    }
    private async Task RenameObjectGroupAsync(LayerTreeRow row)
    {
        if (_layerRenameDialogIsOpen) return; _layerRenameDialogIsOpen = true;
        try
        {
            var name = new TextBox { Text = row.Name, MaxLength = 80, Header = "Group name" };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Rename object group", Content = name, PrimaryButtonText = "Rename", CloseButtonText = "Cancel" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.RenameObjectGroupAsync(row.Id, name.Text, CancellationToken.None);
        }
        finally { _layerRenameDialogIsOpen = false; }
    }
    private async void LayerTree_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!IsObjectModifierDown(VirtualKey.Control) || ShouldIgnoreApplicationShortcut()) return;
        if (args.Key == VirtualKey.G)
        {
            args.Handled = true; var group = ViewModel.LayerTreeRows.FirstOrDefault(row => row.IsSelected && row.Kind is LayerTreeRowKind.Folder or LayerTreeRowKind.ObjectGroup);
            await RunLayerTreeActionAsync(() => IsObjectModifierDown(VirtualKey.Shift) && group is not null ? ViewModel.UngroupTreeRowAsync(group, CancellationToken.None) : ViewModel.GroupTreeSelectionAsync(CancellationToken.None));
        }
        else if (args.Key == VirtualKey.E && ViewModel.CanMergeLayerTree) { args.Handled = true; await RunLayerTreeActionAsync(() => ViewModel.MergeTreeSelectionAsync(CancellationToken.None)); }
        else if (args.Key == VirtualKey.X && ViewModel.HasSelectedAnnotation) { args.Handled = true; ViewModel.CutTreeObjects(); }
        else if (args.Key == VirtualKey.V && ViewModel.CanPasteCutObjects)
        {
            var target = ViewModel.LayerTreeRows.LastOrDefault(row => row.IsSelected && row.Kind == LayerTreeRowKind.Layer);
            if (target is not null) { args.Handled = true; await RunLayerTreeActionAsync(() => ViewModel.PasteTreeObjectsAsync(target.Id, CancellationToken.None)); }
        }
    }
}
