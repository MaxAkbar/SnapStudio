using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.Platform.Windows;

namespace SnapStudio.App.Capture;

/// <summary>Adapts ExactFrame's display labels and searchable, refreshable window list to capture dialogs.</summary>
public sealed class CaptureTargetPicker(Func<XamlRoot?> getRoot) : IWindowsCaptureTargetPicker
{
    public Task<WindowsCaptureTargetChoice?> PickTargetAsync(
        Func<IReadOnlyList<WindowsCaptureTargetChoice>> getTargets,
        CancellationToken cancellationToken) => ShowAsync(
            "Choose a window or display", "Choose what to capture.", "Search windows and displays",
            getTargets, target => target.Title, target => target.Description, cancellationToken);

    public Task<WindowsDisplayMonitor?> PickDisplayAsync(
        Func<IReadOnlyList<WindowsDisplayMonitor>> getMonitors,
        CancellationToken cancellationToken) => ShowAsync(
            "Choose a display", "Displays are numbered from left to right, then top to bottom.",
            "Search displays", getMonitors, monitor => monitor.Title, monitor => monitor.Resolution, cancellationToken);

    public Task<WindowsTopLevelWindow?> PickWindowAsync(
        Func<IReadOnlyList<WindowsTopLevelWindow>> getWindows,
        CancellationToken cancellationToken) => ShowAsync(
            "Choose a window", "Choose an open window to capture. Restore minimized windows to include them in this list.",
            "Search open windows", getWindows, window => window.Title, window => window.Description, cancellationToken);

    private async Task<T?> ShowAsync<T>(
        string title,
        string description,
        string searchLabel,
        Func<IReadOnlyList<T>> getCandidates,
        Func<T, string> getTitle,
        Func<T, string> getDescription,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        XamlRoot root = getRoot() ?? throw new InvalidOperationException("The capture picker is not ready. Try again.");
        IReadOnlyList<T> candidates = getCandidates();
        var search = new TextBox { PlaceholderText = searchLabel };
        AutomationProperties.SetName(search, searchLabel);
        var refresh = new Button { Content = new SymbolIcon(Symbol.Refresh) };
        AutomationProperties.SetName(refresh, "Refresh capture choices");
        ToolTipService.SetToolTip(refresh, "Refresh after opening or closing apps, or connecting a display");
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(refresh, 1);
        searchRow.Children.Add(search);
        searchRow.Children.Add(refresh);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 340 };
        AutomationProperties.SetName(list, "Available capture choices");
        var empty = new TextBlock { Text = "No matching choices. Open an app or connect a display, then refresh.", TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 12, Width = 440, MaxWidth = Math.Max(240, root.Size.Width - 100) };
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(searchRow);
        content.Children.Add(empty);
        content.Children.Add(list);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = content,
            PrimaryButtonText = "Capture",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        void ApplyFilter()
        {
            T? previous = (list.SelectedItem as ListViewItem)?.Tag as T;
            string filter = search.Text.Trim();
            list.Items.Clear();
            // ExactFrame's ApplyWindowFilter matches both window title and process name.
            foreach (T candidate in candidates)
            {
                string heading = getTitle(candidate);
                string detail = getDescription(candidate);
                if (filter.Length > 0 && !heading.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                    && !detail.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) continue;
                var row = new StackPanel { Spacing = 3, Padding = new Thickness(4, 8, 4, 8) };
                row.Children.Add(new TextBlock { Text = heading, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
                var item = new ListViewItem { Content = row, Tag = candidate, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(item, heading);
                AutomationProperties.SetHelpText(item, detail);
                list.Items.Add(item);
                if (Equals(candidate, previous)) list.SelectedItem = item;
            }

            empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is ListViewItem;
        }

        search.TextChanged += (_, _) => ApplyFilter();
        refresh.Click += (_, _) => { candidates = getCandidates(); ApplyFilter(); };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is ListViewItem;
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = list.SelectedItem is not ListViewItem;
        dialog.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        ApplyFilter();
        using var registration = cancellationToken.Register(() => dialog.DispatcherQueue.TryEnqueue(dialog.Hide));
        ContentDialogResult result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary ? (list.SelectedItem as ListViewItem)?.Tag as T : null;
    }
}
