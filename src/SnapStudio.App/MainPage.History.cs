using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _layersSectionExpanded = true;
    private bool _recentCapturesSectionExpanded = true;
    private bool _imagesSectionExpanded = true;

    private void ToggleLayersSection_Click(object sender, RoutedEventArgs e)
    {
        _layersSectionExpanded = !_layersSectionExpanded;
        UpdateSidebarSections();
    }

    private void ToggleRecentCapturesSection_Click(object sender, RoutedEventArgs e)
    {
        _recentCapturesSectionExpanded = !_recentCapturesSectionExpanded;
        UpdateSidebarSections();
    }

    private void ToggleImagesSection_Click(object sender, RoutedEventArgs e)
    {
        _imagesSectionExpanded = !_imagesSectionExpanded;
        UpdateSidebarSections();
    }

    private void HistoryExpandedView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebarSections();

    private void UpdateSidebarSections()
    {
        if (SidebarControlsScroll is null || HistorySettingsButton is null || LayersSectionContent is null
            || RecentCapturesSectionContent is null || ImagesSectionContent is null) return;

        LayersSectionContent.Visibility = _layersSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        RecentCapturesSectionContent.Visibility = _recentCapturesSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        ImagesSectionContent.Visibility = _imagesSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        LayersSectionChevron.Glyph = _layersSectionExpanded ? "\uE70E" : "\uE70D";
        RecentCapturesSectionChevron.Glyph = _recentCapturesSectionExpanded ? "\uE70E" : "\uE70D";
        ImagesSectionChevron.Glyph = _imagesSectionExpanded ? "\uE70E" : "\uE70D";
        AutomationProperties.SetName(LayersSectionButton, _layersSectionExpanded ? "Collapse Layers section" : "Expand Layers section");
        AutomationProperties.SetName(RecentCapturesSectionButton, _recentCapturesSectionExpanded ? "Collapse Recent captures section" : "Expand Recent captures section");
        AutomationProperties.SetName(ImagesSectionButton, _imagesSectionExpanded ? "Collapse Images section" : "Expand Images section");

        SidebarImagesRow.Height = _imagesSectionExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        SidebarImagesRow.MinHeight = _imagesSectionExpanded ? 180 : 0;
        SidebarControlsRow.Height = GridLength.Auto;
        SidebarSettingsRow.Height = _imagesSectionExpanded ? GridLength.Auto : new GridLength(1, GridUnitType.Star);

        // Reserve room for the image list and its fixed page controls before allowing the upper groups to grow.
        double minimumImagesHeight = _imagesSectionExpanded ? 180 : 38;
        double settingsHeight = Math.Max(36, HistorySettingsButton.ActualHeight);
        double availableHeight = HistoryExpandedView.ActualHeight
            - HistoryExpandedView.Padding.Top - HistoryExpandedView.Padding.Bottom
            - HistoryExpandedView.RowSpacing * 2 - settingsHeight - minimumImagesHeight;
        SidebarControlsScroll.MaxHeight = Math.Max(0, availableHeight);
    }

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
