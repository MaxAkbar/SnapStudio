using Microsoft.UI.Xaml.Controls;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _changingInspectorTab;

    private void InspectorTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        // Ignore selection events from controls inside a tab and initialization before the canvas is ready.
        if (_changingInspectorTab || PerspectivePanel is null || PerspectiveHandles is null
            || !args.AddedItems.OfType<TabViewItem>().Any(tab => ReferenceEquals(tab, InspectorTabs.SelectedItem))) return;

        if (ReferenceEquals(InspectorTabs.SelectedItem, GuidesInspectorTab)) ShowPerspectiveGuides();
        else
        {
            PerspectiveHandles.CancelEdit();
            UpdatePerspectiveControls();
        }
    }

    private void ShowInspectorTab(TabViewItem tab)
    {
        _changingInspectorTab = true;
        try { InspectorTabs.SelectedItem = tab; }
        finally { _changingInspectorTab = false; }
        UpdatePerspectiveControls();
    }

    private void ShowActiveToolInspector()
    {
        if (ViewModel.ActivePerspectiveToolIndex > 0) ShowPerspectiveGuides(false);
        else ShowInspectorTab(PropertiesInspectorTab);
    }
}
