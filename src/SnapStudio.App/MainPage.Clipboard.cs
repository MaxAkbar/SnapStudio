using Microsoft.UI.Xaml.Controls;

namespace SnapStudio.App;

public sealed partial class MainPage
{
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
