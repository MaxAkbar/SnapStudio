using Microsoft.UI.Xaml.Controls;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private async void AnnotationFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent() || sender is not ComboBox { SelectedItem: string family }
            || string.Equals(family, ViewModel.AnnotationFontFamily, StringComparison.OrdinalIgnoreCase)) return;
        await ViewModel.UpdateAnnotationFontFamilyAsync(family, CancellationToken.None);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
    }
}
