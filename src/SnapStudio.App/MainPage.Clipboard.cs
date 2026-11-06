using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SnapStudio.Core.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private async void UseClipboardColor_Click(object sender, RoutedEventArgs e)
    {
        if (ShouldIgnoreControlEvent()) return;
        try
        {
            var content = Clipboard.GetContent();
            string? text = content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null;
            if (!ClipboardColor.TryParse(text, out var color))
            {
                ViewModel.ShowCaptureHistoryError("Copy a hex color such as #4CC2FF, then choose Use clipboard color.");
                return;
            }
            await ViewModel.UpdateAnnotationCustomColorAsync(color.R, color.G, color.B, color.A, CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
        catch (Exception error)
        {
            ViewModel.ShowCaptureHistoryError("The clipboard color could not be applied: " + error.Message);
        }
    }

    private async Task ShowPasteImageOptionsAsync()
    {
        if (_pasteImageDialogIsOpen || _newImageDialogIsOpen || _layerRenameDialogIsOpen || _layerColorDialogIsOpen
            || !ViewModel.CanCreateImage) return;

        if (!ViewModel.HasCurrentDocument)
        {
            await PasteImageAndUpdateAsync(CancellationToken.None, asNewDocument: true);
            return;
        }

        _pasteImageDialogIsOpen = true;
        ContentDialogResult result;
        var documentId = ViewModel.CurrentDocumentId;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Paste image",
                Content = new TextBlock
                {
                    Text = "Add the clipboard image as a new layer over this image, or open it as a new image.",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    MaxWidth = 360
                },
                PrimaryButtonText = "New layer",
                SecondaryButtonText = "New image",
                CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = ViewModel.CanPasteImageAsLayer,
                DefaultButton = ContentDialogButton.Primary
            };
            result = await dialog.ShowAsync();
        }
        finally
        {
            _pasteImageDialogIsOpen = false;
        }

        if (result == ContentDialogResult.Primary && ViewModel.CurrentDocumentId == documentId)
            await PasteImageAndUpdateAsync(CancellationToken.None);
        else if (result == ContentDialogResult.Secondary)
            await PasteImageAndUpdateAsync(CancellationToken.None, asNewDocument: true);
    }
}
