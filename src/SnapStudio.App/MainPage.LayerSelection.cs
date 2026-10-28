using Microsoft.UI.Xaml;
using SnapStudio.App.ViewModels.Items;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private async void LayerSelectionAction_Click(object sender, RoutedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || sender is not FrameworkElement { Tag: string action } element) return;
        try { await ViewModel.EditLayerSelectionAsync(action, (element.DataContext as AnnotationLayerItem)?.Id, CancellationToken.None); }
        catch (Exception error) { ViewModel.ShowCaptureHistoryError($"Layer edit failed: {error.Message}"); }
        UpdateBindingsFromViewModel(); CanvasScrollViewer.Focus(FocusState.Programmatic);
    }
}
