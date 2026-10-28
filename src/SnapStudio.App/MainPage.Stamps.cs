using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using Windows.Foundation;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private Flyout? _stampPicker;
    private bool _stampDialogIsOpen;

    private void StampToolButton_Click(object sender, RoutedEventArgs args) => ShowStampPicker();

    private void ShowStampPicker()
    {
        CancelPolygonSelection();
        if (_canvasDragMode != CanvasDragMode.None || _stampDialogIsOpen || !_isReadyForControlEvents || ViewModel.FirstRunSetupIsOpen) return;
        HideToolDesignDrawer();
        _stampPicker?.Hide();
        var content = new StackPanel { Width = 352, Spacing = 10, RequestedTheme = ActualTheme };
        content.Children.Add(new TextBlock { Text = "Stamps", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var search = new TextBox { PlaceholderText = "Search stamps…", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(search, "Search stamps");
        content.Children.Add(search);
        StampDefinition[] library = ViewModel.AvailableStamps.ToArray();
        var category = new ComboBox
        {
            ItemsSource = new[] { "All categories" }.Concat(library.Select(stamp => stamp.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)).ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(category, "Stamp category");
        content.Children.Add(category);
        var gallery = new GridView
        {
            MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            ItemTemplate = (DataTemplate)Resources["StampGalleryItemTemplate"]
        };
        AutomationProperties.SetName(gallery, "Stamp gallery");
        gallery.ItemClick += (_, args) => { if (args.ClickedItem is StampDefinition stamp) SelectStamp(stamp); };
        gallery.ContextRequested += StampGallery_ContextRequested;
        content.Children.Add(gallery);
        var count = new TextBlock { FontSize = 10 };
        content.Children.Add(count);
        void Filter()
        {
            string query = search.Text.Trim();
            string selected = category.SelectedItem as string ?? "All categories";
            StampDefinition[] matches = library.Where(stamp => (category.SelectedIndex == 0 || stamp.Category.Equals(selected, StringComparison.OrdinalIgnoreCase))
                && (stamp.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || stamp.Category.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            gallery.ItemsSource = matches;
            count.Text = matches.Length == 0 ? "No stamps match. Try another search or import an SVG." : $"{matches.Length} {(matches.Length == 1 ? "stamp" : "stamps")} · right-click custom stamps to manage";
        }
        search.TextChanged += (_, _) => Filter();
        category.SelectionChanged += (_, _) => Filter();
        Filter();
        var import = new Button { Content = "Import SVG…", HorizontalAlignment = HorizontalAlignment.Stretch };
        import.Click += ImportStamp_Click;
        content.Children.Add(import);
        var online = new Button { Content = "Search online…", HorizontalAlignment = HorizontalAlignment.Stretch };
        online.Click += async (_, _) => await ShowOnlineStampSearchAsync();
        content.Children.Add(online);
        content.Children.Add(new TextBlock { Text = "Choose a stamp, then click the image to place copies. Size is set in Properties.", TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        _stampPicker = new Flyout { Content = content, Placement = FlyoutPlacementMode.Right };
        _stampPicker.ShowAt(StampToolButton);
    }

    private void SelectStamp(StampDefinition stamp)
    {
        _stampPicker?.Hide();
        ViewModel.UseStamp(stamp);
        ShowActiveToolInspector();
        UpdateBindingsFromViewModel();
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private async void ImportStamp_Click(object sender, RoutedEventArgs args)
    {
        _stampPicker?.Hide();
        try
        {
            if (!await ViewModel.ImportStampAsync(CancellationToken.None)) return;
            ShowActiveToolInspector();
            UpdateBindingsFromViewModel();
            CanvasScrollViewer.Focus(FocusState.Programmatic);
        }
        catch (Exception error) when (error is not OperationCanceledException) { ViewModel.ShowStampError(error.Message); }
    }

    private void StampGallery_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        StampDefinition? stamp = null;
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is FrameworkElement { DataContext: StampDefinition item }) { stamp = item; break; }
        if (stamp is null || ViewModel.IsBuiltInStamp(stamp.Id)) return;
        args.Handled = true;
        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Rename / change category…" };
        edit.Click += async (_, _) => { _stampPicker?.Hide(); await EditStampDetailsAsync(stamp); };
        menu.Items.Add(edit);
        if (stamp.Attribution is { } attribution)
        {
            var source = new MenuFlyoutItem { Text = "Source and license…" };
            source.Click += async (_, _) => { _stampPicker?.Hide(); await ShowStampAttributionAsync(attribution); };
            menu.Items.Add(source);
        }
        var remove = new MenuFlyoutItem { Text = "Remove from stamp library…" };
        remove.Click += async (_, _) => { _stampPicker?.Hide(); await RemoveStampAsync(stamp); };
        menu.Items.Add(remove);
        if (args.TryGetPosition(sender, out Point position)) menu.ShowAt(sender, new FlyoutShowOptions { Position = position });
        else menu.ShowAt((FrameworkElement)sender);
    }

    private async void SaveSelectedVectorAsStamp_Click(object sender, RoutedEventArgs args) => await SaveSelectedVectorAsStampAsync();

    private async Task SaveSelectedVectorAsStampAsync()
    {
        if (ViewModel.SelectedVector is not { } vector) return;
        await EditStampDetailsAsync(new(Guid.NewGuid(), vector.Name, "Custom", vector.Svg) { Attribution = vector.Attribution });
    }

    private async Task EditStampDetailsAsync(StampDefinition stamp)
    {
        if (_stampDialogIsOpen || !CanOpenSvgDesigner) return;
        _stampDialogIsOpen = true;
        try
        {
            var name = new TextBox { Header = "Stamp name", Text = stamp.Name, MaxLength = 80, MinWidth = 280 };
            var category = new TextBox { Header = "Category", Text = stamp.Category, MaxLength = 40 };
            var fields = new StackPanel { Spacing = 12 }; fields.Children.Add(name); fields.Children.Add(category);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Save stamp",
                Content = fields,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            void Validate() => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(category.Text);
            name.TextChanged += (_, _) => Validate(); category.TextChanged += (_, _) => Validate(); Validate();
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.SaveStampAsync(stamp with { Name = name.Text, Category = category.Text }, CancellationToken.None);
        }
        catch (Exception error) { ViewModel.ShowStampError(error.Message); }
        finally { _stampDialogIsOpen = false; }
    }

    private async Task RemoveStampAsync(StampDefinition stamp)
    {
        if (_stampDialogIsOpen || !CanOpenSvgDesigner) return;
        _stampDialogIsOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = $"Remove {stamp.Name}?",
                Content = "Remove this stamp from your library? Copies already placed in images are kept.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.RemoveStampAsync(stamp.Id, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
        catch (Exception error) { ViewModel.ShowStampError(error.Message); }
        finally { _stampDialogIsOpen = false; }
    }
}
