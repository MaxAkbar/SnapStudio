using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.Core.Documents;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _captureTagsDialogIsOpen;

    private async void EditCurrentCaptureTags_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.CurrentDocumentId is not { } id) return;
        CurrentCaptureActionsFlyout.Hide();
        await EditCaptureTagsAsync(id.ToString(), ViewModel.CurrentDocumentTitle);
    }

    private async Task EditCaptureTagsAsync(string documentId, string title)
    {
        if (_captureTagsDialogIsOpen || _captureRenameDialogIsOpen || _captureDeleteDialogIsOpen
            || _historyFolderDialogIsOpen || _newImageDialogIsOpen || _pasteImageDialogIsOpen
            || _layerRenameDialogIsOpen || _layerColorDialogIsOpen || !ViewModel.CanManageCaptureHistory) return;
        _captureTagsDialogIsOpen = true;
        try
        {
            var input = new TextBox
            {
                Header = "Tags", Text = string.Join(", ", ViewModel.GetCaptureTags(documentId)),
                PlaceholderText = "Design, Feedback, To review", AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MinHeight = 76, MaxHeight = 150
            };
            AutomationProperties.SetName(input, "Capture tags");
            ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            var count = new TextBlock { FontSize = 12 };
            var content = new StackPanel { Spacing = 12, MinWidth = 300, MaxWidth = 430 };
            content.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(input);
            content.Children.Add(new TextBlock
            {
                Text = "Separate tags with commas or new lines. Remove a tag from the field to untag this capture.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            });
            string[] available = ViewModel.AvailableCaptureTags.ToArray();
            var search = new AutoSuggestBox
            {
                Header = "Find or add a tag", PlaceholderText = "Search tags by keyword…",
                MaxSuggestionListHeight = 180, HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(search, "Search tags to add");
            AutomationProperties.SetHelpText(search, "Search existing tags by keyword. Choose a suggestion or use Add to add the entered tag.");
            var add = new Button { Content = "Add", VerticalAlignment = VerticalAlignment.Bottom, IsEnabled = false };
            AutomationProperties.SetName(add, "Add tag to capture");
            var searchRow = new Grid { ColumnSpacing = 8 };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.Children.Add(search);
            Grid.SetColumn(add, 1);
            searchRow.Children.Add(add);
            content.Children.Add(searchRow);
            var searchHint = new TextBlock
            {
                Text = "Type a keyword to find saved tags, or enter a new tag and press Add.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            };
            content.Children.Add(searchHint);
            void AddTag(string text)
            {
                try
                {
                    input.Text = string.Join(", ", DocumentTags.Normalize(DocumentTags.Parse(input.Text).Concat(DocumentTags.Parse(text))));
                    search.Text = string.Empty;
                    search.IsSuggestionListOpen = false;
                    search.Focus(FocusState.Programmatic);
                }
                catch (ArgumentException exception) { error.Text = exception.Message; error.Visibility = Visibility.Visible; }
            }
            search.TextChanged += (_, args) =>
            {
                add.IsEnabled = !string.IsNullOrWhiteSpace(search.Text);
                if (args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen) return;
                string keyword = search.Text.Trim();
                string[] assigned;
                try { assigned = DocumentTags.Parse(input.Text); }
                catch (ArgumentException) { assigned = []; }
                var assignedTags = assigned.ToHashSet(StringComparer.OrdinalIgnoreCase);
                string[] matches = keyword.Length == 0 ? [] : available
                    .Where(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        && !assignedTags.Contains(tag)).ToArray();
                search.ItemsSource = matches.Take(20).ToArray();
                search.IsSuggestionListOpen = matches.Length > 0;
                searchHint.Text = keyword.Length == 0
                    ? "Type a keyword to find saved tags, or enter a new tag and press Add."
                    : matches.Length == 0 ? "No matching unassigned tags. Use Add to create this tag."
                    : matches.Length > 20 ? $"{matches.Length} matching tags. Showing the first 20; keep typing to narrow the results."
                    : $"{matches.Length} matching {(matches.Length == 1 ? "tag" : "tags")}. Choose a suggestion to add it.";
            };
            search.QuerySubmitted += (_, args) => AddTag(args.ChosenSuggestion as string ?? args.QueryText);
            add.Click += (_, _) => AddTag(search.Text);
            content.Children.Add(count);
            content.Children.Add(error);
            content.Children.Add(new TextBlock
            {
                Text = "Find tagged images in library search. Use #Design to match an exact tag.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            });
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Edit capture tags",
                Content = content, PrimaryButtonText = "Save", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.None
            };
            void Validate()
            {
                try
                {
                    int number = DocumentTags.Parse(input.Text).Length;
                    count.Text = $"{number} {(number == 1 ? "tag" : "tags")} · {DocumentTags.MaximumLength} characters per tag";
                    error.Text = string.Empty;
                    error.Visibility = Visibility.Collapsed;
                    dialog.IsPrimaryButtonEnabled = true;
                }
                catch (ArgumentException exception)
                {
                    error.Text = exception.Message;
                    error.Visibility = Visibility.Visible;
                    dialog.IsPrimaryButtonEnabled = false;
                }
            }
            input.TextChanged += (_, _) => Validate();
            Validate();
            dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await RunCaptureHistoryUiActionAsync(() => ViewModel.SetCaptureTagsAsync(documentId, DocumentTags.Parse(input.Text), CancellationToken.None));
        }
        catch (Exception exception) { ViewModel.ShowCaptureHistoryError(exception.Message); }
        finally { _captureTagsDialogIsOpen = false; }
    }
}
