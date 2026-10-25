using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.App.Controls;
using SnapStudio.Core.Documents;
using Windows.System;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private async Task ShowOnlineStampSearchAsync()
    {
        if (!CanOpenSvgDesigner || !_isReadyForControlEvents) return;
        _stampPicker?.Hide();
        _stampDialogIsOpen = true;
        using var lifetime = new CancellationTokenSource();
        CancellationTokenSource? searchRequest = null;
        CancellationTokenSource? previewRequest = null;
        bool closed = false, saving = false;
        int pageStart = 0;
        bool hasMore = false;
        string? selectedSvg = null;
        StampDefinition? added = null;
        double width = Math.Max(320, Math.Min(740, XamlRoot.Size.Width - 120));
        var query = new TextBox { PlaceholderText = "Try check, warning, arrow…", MaxLength = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(query, "Search online icons");
        var search = new Button { Content = "Search", IsEnabled = false };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.Children.Add(query); Grid.SetColumn(search, 1); searchRow.Children.Add(search);
        var collection = new ComboBox
        {
            Header = "Collection",
            ItemsSource = new[] { "All collections", "Heroicons", "Tabler", "Lucide" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(collection, "Online icon collection");
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)Resources["OnlineStampItemTemplate"],
            Height = Math.Max(140, Math.Min(290, XamlRoot.Size.Height - 460))
        };
        AutomationProperties.SetName(results, "Online icon results");
        var thumbnail = new SvgThumbnail { ThumbnailSize = 112, HorizontalAlignment = HorizontalAlignment.Center };
        var description = new TextBlock { Text = "Select a result to preview it.", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 12 };
        var sourceLink = new HyperlinkButton { Content = "View icon source", Visibility = Visibility.Collapsed, Padding = new(0) };
        var licenseLink = new HyperlinkButton { Content = "View license", Visibility = Visibility.Collapsed, Padding = new(0) };
        var preview = new StackPanel { Spacing = 8, MaxWidth = 230 };
        foreach (UIElement child in new UIElement[] { thumbnail, description, sourceLink, licenseLink }) preview.Children.Add(child);
        var browser = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        browser.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (width >= 600)
        {
            browser.ColumnDefinitions.Add(new() { Width = new GridLength(230) });
            Grid.SetColumn(preview, 1);
        }
        else
        {
            browser.RowDefinitions.Add(new() { Height = GridLength.Auto });
            browser.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(preview, 1); thumbnail.ThumbnailSize = 64;
        }
        browser.Children.Add(results); browser.Children.Add(preview);
        var previous = new Button { Content = "Previous", IsEnabled = false };
        var next = new Button { Content = "Next", IsEnabled = false };
        var page = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var paging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        paging.Children.Add(previous); paging.Children.Add(next); paging.Children.Add(page);
        var status = new TextBlock { Text = "Enter a keyword, then press Search.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var content = new StackPanel { Width = width, Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Search terms and selected icon IDs are sent to Iconify. Your images stay on your PC. Saved stamps work offline.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });
        foreach (UIElement child in new UIElement[] { searchRow, collection, browser, paging, status }) content.Children.Add(child);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Search online · Iconify",
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(220, XamlRoot.Size.Height - 230), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = "Add to stamps",
            IsPrimaryButtonEnabled = false,
            CloseButtonText = "Cancel"
        };
        dialog.Resources["ContentDialogMaxWidth"] = width + 64;

        void UpdateControls()
        {
            bool searching = searchRequest is not null;
            search.IsEnabled = !saving && !string.IsNullOrWhiteSpace(query.Text);
            previous.IsEnabled = !saving && !searching && pageStart > 0;
            next.IsEnabled = !saving && !searching && hasMore;
            results.IsEnabled = !saving;
            query.IsEnabled = collection.IsEnabled = !saving;
            dialog.IsPrimaryButtonEnabled = !saving && selectedSvg is not null && results.SelectedItem is OnlineStampIcon;
        }
        void ClearPreview()
        {
            previewRequest?.Cancel();
            selectedSvg = null; thumbnail.Svg = string.Empty;
            description.Text = "Select a result to preview it.";
            sourceLink.Visibility = licenseLink.Visibility = Visibility.Collapsed;
            UpdateControls();
        }
        void InputsChanged()
        {
            searchRequest?.Cancel(); searchRequest = null;
            ClearPreview(); results.ItemsSource = null; pageStart = 0; hasMore = false; page.Text = string.Empty;
            status.Text = "Press Search to find icons."; UpdateControls();
        }
        async Task PreviewAsync()
        {
            ClearPreview();
            if (results.SelectedItem is not OnlineStampIcon icon) return;
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            previewRequest = request;
            description.Text = $"{icon.Name}\n{icon.Collection}\nLoading preview…";
            try
            {
                string svg = await ViewModel.PreviewOnlineStampAsync(icon, request.Token);
                if (closed || request.IsCancellationRequested || !ReferenceEquals(results.SelectedItem, icon)) return;
                StampAttribution attribution = StampCatalog.ValidateAttribution(icon.Attribution);
                thumbnail.Svg = svg; selectedSvg = svg;
                description.Text = $"{icon.Name}\n{attribution.Collection}\nBy {attribution.Author}\nLicense: {attribution.License}";
                sourceLink.NavigateUri = new(attribution.SourceUrl); licenseLink.NavigateUri = new(attribution.LicenseUrl);
                sourceLink.Visibility = licenseLink.Visibility = Visibility.Visible;
                status.Text = "Add this icon to your saved stamps, then click the image to place it.";
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!closed && !request.IsCancellationRequested) { description.Text = "Preview unavailable."; status.Text = error.Message + " Select the result again to retry."; }
            }
            finally
            {
                if (ReferenceEquals(previewRequest, request)) previewRequest = null;
                if (!closed) UpdateControls();
            }
        }
        async Task SearchAsync(int start)
        {
            if (saving || string.IsNullOrWhiteSpace(query.Text)) return;
            searchRequest?.Cancel(); ClearPreview();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            searchRequest = request;
            string term = query.Text.Trim();
            string? prefix = collection.SelectedIndex switch { 1 => "heroicons", 2 => "tabler", 3 => "lucide", _ => null };
            results.ItemsSource = null; hasMore = false;
            status.Text = "Searching Iconify…"; UpdateControls();
            try
            {
                OnlineStampSearchResult found = await ViewModel.SearchOnlineStampsAsync(term, prefix, start, request.Token);
                if (closed || request.IsCancellationRequested || !ReferenceEquals(searchRequest, request)) return;
                pageStart = start; hasMore = found.HasMore;
                page.Text = $"Page {start / 32 + 1}";
                results.ItemsSource = found.Icons;
                status.Text = found.Icons.Count == 0 ? "No icons found. Try another keyword or collection." : $"{found.Icons.Count} results. Select an icon to preview it.";
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error) { if (!closed && !request.IsCancellationRequested) status.Text = error.Message + " Press Search to retry."; }
            finally
            {
                if (ReferenceEquals(searchRequest, request)) searchRequest = null;
                if (!closed) UpdateControls();
            }
        }
        query.TextChanged += (_, _) => InputsChanged();
        collection.SelectionChanged += (_, _) => InputsChanged();
        query.KeyDown += async (_, args) => { if (args.Key == VirtualKey.Enter) { args.Handled = true; await SearchAsync(0); } };
        search.Click += async (_, _) => await SearchAsync(0);
        previous.Click += async (_, _) => await SearchAsync(Math.Max(0, pageStart - 32));
        next.Click += async (_, _) => await SearchAsync(pageStart + 32);
        results.SelectionChanged += async (_, _) => await PreviewAsync();
        results.ItemClick += async (_, args) =>
        {
            if (selectedSvg is null && previewRequest is null && ReferenceEquals(results.SelectedItem, args.ClickedItem)) await PreviewAsync();
        };
        results.IsItemClickEnabled = true;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (saving || selectedSvg is null || results.SelectedItem is not OnlineStampIcon icon) return;
            var deferral = args.GetDeferral();
            saving = true; UpdateControls();
            try
            {
                added = await ViewModel.SaveOnlineStampAsync(icon, selectedSvg, lifetime.Token);
                args.Cancel = false;
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { saving = false; UpdateControls(); deferral.Complete(); }
        };
        dialog.CloseButtonClick += (_, args) => { if (saving) args.Cancel = true; };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        try
        {
            await dialog.ShowAsync();
            if (added is not null) SelectStamp(added);
        }
        catch (Exception error) { ViewModel.ShowStampError(error.Message); }
        finally { closed = true; lifetime.Cancel(); _stampDialogIsOpen = false; }
    }

    private async void SelectedStampAttribution_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedVector?.Attribution is { } attribution) await ShowStampAttributionAsync(attribution);
    }

    private async Task ShowStampAttributionAsync(StampAttribution source)
    {
        if (!CanOpenSvgDesigner) return;
        _stampDialogIsOpen = true;
        try
        {
            source = StampCatalog.ValidateAttribution(source);
            var content = new StackPanel { Spacing = 10, MaxWidth = 400 };
            content.Children.Add(new TextBlock
            {
                Text = $"{source.IconId}\n{source.Collection}\nBy {source.Author}\nLicense: {source.License}\nProvider: {source.Provider}",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });
            content.Children.Add(new HyperlinkButton { Content = "View original icon", NavigateUri = new(source.SourceUrl), Padding = new(0) });
            content.Children.Add(new HyperlinkButton { Content = "View license terms", NavigateUri = new(source.LicenseUrl), Padding = new(0) });
            content.Children.Add(new TextBlock { Text = "This is the original artwork's source information. Your copy may have been edited. Keep the applicable notices when redistributing artwork.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            await new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Source and license", Content = content, CloseButtonText = "Close" }.ShowAsync();
        }
        catch (Exception error) { ViewModel.ShowStampError(error.Message); }
        finally { _stampDialogIsOpen = false; }
    }
}
