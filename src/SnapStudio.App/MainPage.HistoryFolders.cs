using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SnapStudio.App.ViewModels.Items;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _historyFolderDialogIsOpen;
    private bool _captureDeleteDialogIsOpen;
    private bool _captureRenameDialogIsOpen;

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
        if (ViewModel.SelectedHistoryFolder is { Id: not null } folder) await RenameHistoryFolderItemAsync(folder);
    }

    private async Task RenameHistoryFolderItemAsync(HistoryFolderItem folder)
    {
        if (folder.Id is not Guid id) return;
        await RunHistoryFolderDialogAsync(async () =>
        {
            string? name = await AskHistoryFolderNameAsync("Rename folder", folder.Name, renameFolderId: id);
            if (name is not null) await ViewModel.RenameHistoryFolderAsync(id, name, CancellationToken.None);
        });
    }

    private async void RemoveHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHistoryFolder is { Id: not null } folder) await RemoveHistoryFolderItemAsync(folder);
    }

    private Task RemoveHistoryFolderItemAsync(HistoryFolderItem folder) => folder.Id is Guid id
        ? RunHistoryFolderDialogAsync(() => ViewModel.RemoveHistoryFolderAsync(id, CancellationToken.None)) : Task.CompletedTask;

    private void HistoryFolderItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (!ViewModel.CanManageHistoryFolders || sender is not FrameworkElement { DataContext: HistoryFolderItem { Id: not null } folder }) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = folder.Name, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        var rename = new MenuFlyoutItem { Text = "Rename…", Icon = new SymbolIcon(Symbol.Edit) };
        rename.Click += async (_, _) => await RenameHistoryFolderItemAsync(folder);
        menu.Items.Add(rename);
        var remove = new MenuFlyoutItem
        {
            Text = folder.SourcePath is null ? "Remove group (keep captures)" : "Remove folder mapping (keep captures)",
            Icon = new SymbolIcon(Symbol.Remove)
        };
        string help = folder.SourcePath is null
            ? "Remove this group and move its captures to Unfiled. Image files are kept."
            : "Stop mapping this folder and move its captures to Unfiled. Original files and editable copies are kept.";
        ToolTipService.SetToolTip(remove, help);
        AutomationProperties.SetHelpText(remove, help);
        remove.Click += async (_, _) => await RemoveHistoryFolderItemAsync(folder);
        menu.Items.Add(remove);
        bool hasPosition = args.TryGetPosition(HistoryFolderPicker, out var position);
        args.Handled = true;
        void ShowMenu()
        {
            if (HistoryFolderPicker.XamlRoot is null) return;
            if (hasPosition) menu.ShowAt(HistoryFolderPicker, position);
            else menu.ShowAt(HistoryFolderPicker);
        }
        void PickerClosed(object? source, object eventArgs)
        {
            HistoryFolderPicker.DropDownClosed -= PickerClosed;
            ShowMenu();
        }
        // Anchor the menu to the picker after its popup closes, so removing or
        // renaming an entry cannot leave the flyout attached to an unloaded row.
        if (HistoryFolderPicker.IsDropDownOpen)
        {
            HistoryFolderPicker.DropDownClosed += PickerClosed;
            HistoryFolderPicker.IsDropDownOpen = false;
        }
        else ShowMenu();
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
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Move capture to folder", Content = picker,
                PrimaryButtonText = "Move", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && picker.SelectedItem is HistoryFolderItem folder)
                await ViewModel.MoveCaptureToHistoryFolderAsync(id.ToString(), folder.Id, CancellationToken.None);
        });
    }

    private void CaptureHistoryItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (!ViewModel.CanManageCaptureHistory || sender is not FrameworkElement { DataContext: DocumentSummaryItem capture } element) return;
        CreateCaptureHistoryMenu(capture).ShowAt(element);
        args.Handled = true;
    }

    private MenuFlyout CreateCaptureHistoryMenu(DocumentSummaryItem capture)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = capture.Title, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        var rename = new MenuFlyoutItem { Text = "Rename…", Icon = new SymbolIcon(Symbol.Edit) };
        rename.Click += async (_, _) => await RenameHistoryCaptureAsync(capture);
        menu.Items.Add(rename);
        var tags = new MenuFlyoutItem { Text = "Edit tags…", Icon = new SymbolIcon(Symbol.Tag) };
        tags.Click += async (_, _) => await EditCaptureTagsAsync(capture.Id, capture.Title);
        menu.Items.Add(tags);
        var duplicate = new MenuFlyoutItem { Text = "Duplicate", Icon = new SymbolIcon(Symbol.Copy) };
        duplicate.Click += async (_, _) => await RunCaptureHistoryUiActionAsync(
            () => ViewModel.DuplicateCaptureAsync(capture.Id, CancellationToken.None));
        menu.Items.Add(duplicate);
        var move = new MenuFlyoutSubItem { Text = "Move to folder", Icon = new SymbolIcon(Symbol.Folder), IsEnabled = ViewModel.CanManageHistoryFolders };
        Guid? current = ViewModel.GetCaptureHistoryFolderId(capture.Id);
        foreach (HistoryFolderItem folder in ViewModel.HistoryFolders.Where(folder => folder.IsUnfiled || folder.Id.HasValue))
        {
            var item = new MenuFlyoutItem { Text = folder.Name, IsEnabled = folder.Id != current };
            item.Click += async (_, _) => await RunCaptureHistoryUiActionAsync(
                () => ViewModel.MoveCaptureToHistoryFolderAsync(capture.Id, folder.Id, CancellationToken.None));
            move.Items.Add(item);
        }
        menu.Items.Add(move);
        var reveal = new MenuFlyoutItem
        {
            Text = "Show in folder", Icon = new SymbolIcon(Symbol.OpenLocal),
            IsEnabled = ViewModel.CanShowCaptureInFolder(capture.Id)
        };
        const string revealHelp = "Show this capture's stored image file in File Explorer.";
        ToolTipService.SetToolTip(reveal, revealHelp);
        AutomationProperties.SetHelpText(reveal, revealHelp);
        reveal.Click += async (_, _) => await RunCaptureHistoryUiActionAsync(
            () => ViewModel.ShowCaptureInFolderAsync(capture.Id, CancellationToken.None));
        menu.Items.Add(reveal);
        menu.Items.Add(new MenuFlyoutSeparator());
        var remove = new MenuFlyoutItem { Text = "Remove from recent captures", Icon = new SymbolIcon(Symbol.Remove) };
        const string removeHelp = "Remove this capture from the list and keep its image file.";
        ToolTipService.SetToolTip(remove, removeHelp);
        AutomationProperties.SetHelpText(remove, removeHelp);
        remove.Click += async (_, _) => await RunCaptureHistoryUiActionAsync(
            () => ViewModel.RemoveCaptureFromHistoryAsync(capture.Id, CancellationToken.None));
        menu.Items.Add(remove);
        var delete = new MenuFlyoutItem
        {
            Text = "Delete image (Recycle Bin)…", Icon = new SymbolIcon(Symbol.Delete),
            IsEnabled = ViewModel.CanRecycleCaptureImage(capture.Id)
        };
        const string deleteHelp = "Move this capture's image file to the Recycle Bin and remove it from the list.";
        ToolTipService.SetToolTip(delete, deleteHelp);
        AutomationProperties.SetHelpText(delete, deleteHelp);
        delete.Click += async (_, _) => await ConfirmDeleteCaptureImageAsync(capture.Id, capture.Title);
        menu.Items.Add(delete);
        return menu;
    }

    private async Task RenameHistoryCaptureAsync(DocumentSummaryItem capture)
    {
        if (_captureTagsDialogIsOpen || _captureRenameDialogIsOpen || _captureDeleteDialogIsOpen || _historyFolderDialogIsOpen
            || _newImageDialogIsOpen || _pasteImageDialogIsOpen || _layerRenameDialogIsOpen
            || _layerColorDialogIsOpen || !ViewModel.CanManageCaptureHistory) return;
        _captureRenameDialogIsOpen = true;
        try
        {
            var input = new TextBox { Text = capture.Title, MinWidth = 280, Header = "Capture name" };
            AutomationProperties.SetName(input, "Capture name");
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(input);
            content.Children.Add(error);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Rename capture",
                Content = content, PrimaryButtonText = "Rename", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(input.Text)) return;
                error.Text = "Enter a capture name.";
                args.Cancel = true;
            };
            dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await RunCaptureHistoryUiActionAsync(() => ViewModel.RenameCaptureAsync(capture.Id, input.Text, CancellationToken.None));
        }
        catch (Exception exception) { ViewModel.ShowCaptureHistoryError(exception.Message); }
        finally { _captureRenameDialogIsOpen = false; }
    }

    private async Task RunCaptureHistoryUiActionAsync(Func<Task> action)
    {
        var previousDocumentId = ViewModel.CurrentDocumentId;
        try { await action(); }
        catch (Exception exception) { ViewModel.ShowCaptureHistoryError(exception.Message); }
        finally
        {
            UpdateBindingsFromViewModel();
            if (ViewModel.CurrentDocumentId != previousDocumentId) FitCurrentCanvas();
        }
    }

    private async Task ConfirmDeleteCaptureImageAsync(string captureId, string title)
    {
        if (_captureTagsDialogIsOpen || _captureDeleteDialogIsOpen || _captureRenameDialogIsOpen || _historyFolderDialogIsOpen || _newImageDialogIsOpen
            || _pasteImageDialogIsOpen || _layerRenameDialogIsOpen || _layerColorDialogIsOpen
            || !ViewModel.CanRecycleCaptureImage(captureId)) return;
        _captureDeleteDialogIsOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                Title = "Delete image?",
                Content = new TextBlock
                {
                    Text = $"Move the image file for ‘{title}’ to the Recycle Bin and remove this capture from the list?\n\nTo keep the file, cancel and choose Remove from recent captures.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Move to Recycle Bin", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await RunCaptureHistoryUiActionAsync(() => ViewModel.MoveCaptureToTrashAsync(captureId, CancellationToken.None));
        }
        catch (Exception exception) { ViewModel.ShowCaptureHistoryError(exception.Message); }
        finally { _captureDeleteDialogIsOpen = false; }
    }

    private async Task RunHistoryFolderDialogAsync(Func<Task> action)
    {
        if (_captureTagsDialogIsOpen || _historyFolderDialogIsOpen || _captureDeleteDialogIsOpen || _captureRenameDialogIsOpen || _newImageDialogIsOpen || _pasteImageDialogIsOpen
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

    private async Task<string?> AskHistoryFolderNameAsync(string title, string value, Guid? renameFolderId = null,
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
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = title, Content = content,
            PrimaryButtonText = title == "Import folder" ? "Import" : title == "Add folder" ? "Add folder" : "Save",
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { ViewModel.ValidateHistoryFolderName(input.Text, renameFolderId); }
            catch (ArgumentException exception) { error.Text = exception.Message; args.Cancel = true; }
        };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }
}
