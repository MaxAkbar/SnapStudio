using Microsoft.UI.Xaml;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private void PreviousHistoryPage_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowPreviousHistoryPage();
        UpdateBindingsFromViewModel();
        ScrollCaptureHistoryToStart();
    }

    private void NextHistoryPage_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowNextHistoryPage();
        UpdateBindingsFromViewModel();
        ScrollCaptureHistoryToStart();
    }

    private void ScrollCaptureHistoryToStart()
    {
        if (ViewModel.RecentDocuments.Count > 0) CaptureHistoryList.ScrollIntoView(ViewModel.RecentDocuments[0]);
    }
}
