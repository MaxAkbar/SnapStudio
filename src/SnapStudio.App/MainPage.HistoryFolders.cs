using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SnapStudio.App.ViewModels.Items;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _historyFolderDialogIsOpen;

    private void HistoryFolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindings || sender is not ComboBox { SelectedIndex: >= 0 } picker) return;
        ViewModel.SelectHistoryFolder(picker.SelectedIndex);
        ScrollCaptureHistoryToStart();
    }

    private async void AddMappedHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        await RunHistoryFolderDialogAsync(async () =>
        {
            string? path = await ViewModel.PickHistoryImportFolderAsync(CancellationToken.None);
            if (path is null || ViewModel.TrySelectMappedHistoryFolder(path)) return;
            var subfolders = new CheckBox { Content = "Include images in subfolders", IsChecked = false };
            string suggested = ViewModel.SuggestHistoryFolderName(System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)));
            string? name = await AskHistoryFolderNameAsync("Add folder", suggested, extraContent: subfolders,
                description: "Keep this folder as a group. SnapStudio copies its images for editing. Use Refresh to add new images later; your originals stay in place.");
            if (name is not null) await ViewModel.AddMappedHistoryFolderAsync(path, name, subfolders.IsChecked == true, CancellationToken.None);
        });
    }

    private async void RefreshHistoryFolders_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshHistoryFoldersAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
        ScrollCaptureHistoryToStart();
    }

    private async void NewHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        await RunHistoryFolderDialogAsync(async () =>
        {
            string? name = await AskHistoryFolderNameAsync("New folder", ViewModel.SuggestHistoryFolderName("New folder"));
            if (name is not null) await ViewModel.CreateHistoryFolderAsync(name, CancellationToken.None);
        });
    }

    private async void RenameHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditSelectedHistoryFolder) return;
        await RunHistoryFolderDialogAsync(async () =>
        {
            string? name = await AskHistoryFolderNameAsync("Rename folder", ViewModel.SelectedHistoryFolder!.Name, rename: true);
            if (name is not null) await ViewModel.RenameHistoryFolderAsync(name, CancellationToken.None);
        });
    }

    private async void RemoveHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanEditSelectedHistoryFolder) await ViewModel.RemoveHistoryFolderAsync(CancellationToken.None);
    }

    private async void ImportHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        await RunHistoryFolderDialogAsync(async () =>
        {
            string? path = await ViewModel.PickHistoryImportFolderAsync(CancellationToken.None);
            if (path is null) return;
            var subfolders = new CheckBox { Content = "Include images in subfolders", IsChecked = false };
            string suggested = ViewModel.SuggestHistoryFolderName(System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)));
            string? name = await AskHistoryFolderNameAsync("Import folder", suggested, extraContent: subfolders,
                description: "Copy PNG, JPEG, and BMP images into a new group. Your original files stay in place.");
            if (name is not null) await ViewModel.ImportHistoryFolderAsync(path, name, subfolders.IsChecked == true, CancellationToken.None);
        });
    }

    private void CancelHistoryFolderImport_Click(object sender, RoutedEventArgs e) => ViewModel.CancelHistoryFolderImport();

    private async void MoveCurrentCaptureToFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanMoveCurrentCaptureToHistoryFolder || ViewModel.CurrentDocumentId is not { } id) return;
        await RunHistoryFolderDialogAsync(async () =>
        {
            HistoryFolderItem[] destinations = ViewModel.HistoryFolders.Where(folder => folder.IsUnfiled || folder.Id.HasValue).ToArray();
            Guid? current = ViewModel.GetCaptureHistoryFolderId(id.ToString());
            var picker = new ComboBox
            {
                ItemsSource = destinations,
                DisplayMemberPath = nameof(HistoryFolderItem.DisplayName),
                SelectedIndex = Math.Max(0, Array.FindIndex(destinations, folder => folder.Id == current)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(picker, "Destination capture folder");
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Move capture to folder", Content = picker,
                PrimaryButtonText = "Move", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && picker.SelectedItem is HistoryFolderItem folder)
                await ViewModel.MoveCaptureToHistoryFolderAsync(id.ToString(), folder.Id, CancellationToken.None);
        });
    }

    private void CaptureHistoryItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (!ViewModel.CanManageHistoryFolders || sender is not FrameworkElement { DataContext: DocumentSummaryItem capture } element) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = capture.Title, IsEnabled = false });
        var move = new MenuFlyoutSubItem { Text = "Move to folder" };
        Guid? current = ViewModel.GetCaptureHistoryFolderId(capture.Id);
        foreach (HistoryFolderItem folder in ViewModel.HistoryFolders.Where(folder => folder.IsUnfiled || folder.Id.HasValue))
        {
            var item = new MenuFlyoutItem { Text = folder.Name, IsEnabled = folder.Id != current };
            item.Click += async (_, _) => await ViewModel.MoveCaptureToHistoryFolderAsync(capture.Id, folder.Id, CancellationToken.None);
            move.Items.Add(item);
        }
        menu.Items.Add(move);
        menu.ShowAt(element);
        args.Handled = true;
    }

    private async Task RunHistoryFolderDialogAsync(Func<Task> action)
    {
        if (_historyFolderDialogIsOpen || _newImageDialogIsOpen || _pasteImageDialogIsOpen
            || _layerRenameDialogIsOpen || _layerColorDialogIsOpen || !ViewModel.CanManageHistoryFolders) return;
        _historyFolderDialogIsOpen = true;
        try { await action(); }
        catch (Exception exception) { ViewModel.ShowHistoryFolderError(exception.Message); }
        finally
        {
            _historyFolderDialogIsOpen = false;
            UpdateBindingsFromViewModel();
            ScrollCaptureHistoryToStart();
        }
    }

    private async Task<string?> AskHistoryFolderNameAsync(string title, string value, bool rename = false,
        UIElement? extraContent = null, string? description = null)
    {
        var input = new TextBox { Text = value, MaxLength = 80, PlaceholderText = "Folder name" };
        AutomationProperties.SetName(input, "Capture folder name");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Resources["SnapMutedBrush"] };
        var content = new StackPanel { Spacing = 12 };
        if (description is not null) content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(input);
        if (extraContent is not null) content.Children.Add(extraContent);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = content,
            PrimaryButtonText = title == "Import folder" ? "Import" : title == "Add folder" ? "Add folder" : "Save",
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { ViewModel.ValidateHistoryFolderName(input.Text, rename); }
            catch (ArgumentException exception) { error.Text = exception.Message; args.Cancel = true; }
        };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }
}
