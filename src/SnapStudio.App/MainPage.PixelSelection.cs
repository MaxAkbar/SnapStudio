using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.Foundation;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _selectionFillDialogIsOpen;

    private void SelectionToolButton_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        ShowAreaSelectionTools();
    }

    private void ShowAreaSelectionTools()
    {
        CancelPolygonSelection();
        if (_canvasDragMode != CanvasDragMode.None) return;
        var menu = new MenuFlyout();
        PopulateAreaSelectionTools(menu.Items);
        _objectContextMenu?.Hide();
        _objectContextMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(menu, _objectContextMenu)) _objectContextMenu = null; };
        menu.ShowAt(SelectionToolButton);
    }

    private void PopulateAreaSelectionTools(IList<MenuFlyoutItemBase> items)
    {
        string[] names = ["Rectangular marquee", "Elliptical marquee", "Lasso", "Magic wand", "Polygonal lasso"];
        string group = Guid.NewGuid().ToString();
        for (int index = 0; index < names.Length; index++)
        {
            int selected = index;
            var item = new RadioMenuFlyoutItem
            {
                Text = names[index],
                GroupName = group,
                IsChecked = ViewModel.IsMarqueeToolActive && ViewModel.PixelSelectionToolIndex == index
            };
            item.Click += (_, _) => { ViewModel.PixelSelectionToolIndex = selected; SelectCanvasTool(null); };
            items.Add(item);
        }
    }

    private void ShowPixelSelectionContextMenu(Point position)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = ViewModel.SelectionLayerLabel, IsEnabled = false });
        var mask = new MenuFlyoutItem { Text = "Add layer mask", Tag = "Mask", IsEnabled = ViewModel.CanApplyLayerSelection };
        mask.Click += LayerSelectionAction_Click; menu.Items.Add(mask);
        var crop = new MenuFlyoutItem { Text = "Crop layer to selection", Tag = "Crop", IsEnabled = ViewModel.CanApplyLayerSelection };
        crop.Click += LayerSelectionAction_Click; menu.Items.Add(crop);
        menu.Items.Add(new MenuFlyoutSeparator());
        var copy = new MenuFlyoutItem { Text = "Copy selection", KeyboardAcceleratorTextOverride = "Ctrl+C" };
        copy.Click += async (_, _) => await CopyCurrentDocumentAsync(CancellationToken.None);
        menu.Items.Add(copy);
        var fill = new MenuFlyoutItem { Text = "Fill selection…", KeyboardAcceleratorTextOverride = "Shift+F5" };
        fill.Click += FillPixelSelection_Click;
        menu.Items.Add(fill);
        var invert = new MenuFlyoutItem { Text = "Invert selection", KeyboardAcceleratorTextOverride = "Ctrl+Shift+I" };
        invert.Click += InvertPixelSelection_Click;
        menu.Items.Add(invert);
        var all = new MenuFlyoutItem { Text = "Select all", KeyboardAcceleratorTextOverride = "Ctrl+A" };
        all.Click += (_, _) => { ViewModel.SelectEntireCanvas(); UpdateBindingsFromViewModel(); };
        menu.Items.Add(all);
        var deselect = new MenuFlyoutItem { Text = "Deselect", KeyboardAcceleratorTextOverride = "Ctrl+D" };
        deselect.Click += DeselectPixelSelection_Click;
        menu.Items.Add(deselect);
        menu.Items.Add(new MenuFlyoutSeparator());
        var tools = new MenuFlyoutSubItem { Text = "Selection tool" };
        PopulateAreaSelectionTools(tools.Items);
        menu.Items.Add(tools);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateGridSettingsMenuItem(position));
        _objectContextMenu?.Hide();
        _objectContextMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(menu, _objectContextMenu)) _objectContextMenu = null; };
        menu.ShowAt(this, new FlyoutShowOptions { Position = position });
    }

    private void InvertPixelSelection_Click(object sender, RoutedEventArgs args)
    {
        ViewModel.InvertPixelSelection(); UpdateBindingsFromViewModel();
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private void DeselectPixelSelection_Click(object sender, RoutedEventArgs args)
    {
        ViewModel.ClearAnnotationSelection(); UpdateBindingsFromViewModel();
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private void InvertSelectionKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode == CanvasDragMode.None && TryHandleApplicationShortcut(args, ViewModel.SourcePixelSelection is not null))
            InvertPixelSelection_Click(sender, new());
    }

    private void FillSelectionKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode == CanvasDragMode.None && TryHandleApplicationShortcut(args, ViewModel.SourcePixelSelection is not null))
            FillPixelSelection_Click(sender, new());
    }

    private async void FillPixelSelection_Click(object sender, RoutedEventArgs args)
    {
        if (_selectionFillDialogIsOpen || ViewModel.SourcePixelSelection is not { } selection) return;
        var documentId = ViewModel.CurrentDocumentId;
        _selectionFillDialogIsOpen = true;
        try
        {
            var picker = new ColorPicker
            {
                IsAlphaEnabled = true,
                IsHexInputVisible = true,
                Color = Windows.UI.Color.FromArgb(255, 76, 194, 255),
                MinWidth = 280
            };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Fill selection",
                Content = picker,
                PrimaryButtonText = "Fill",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && documentId == ViewModel.CurrentDocumentId && ReferenceEquals(selection, ViewModel.SourcePixelSelection))
            {
                var color = picker.Color;
                await ViewModel.FillPixelSelectionAsync(new ColorRgba(color.R, color.G, color.B, color.A), CancellationToken.None);
                UpdateBindingsFromViewModel();
            }
        }
        finally { _selectionFillDialogIsOpen = false; }
    }
}
