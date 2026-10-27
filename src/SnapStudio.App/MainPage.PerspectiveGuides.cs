using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.App.Controls;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _guidesInspectorOpen;
    private int _lastPerspectiveTool;
    private object? _guideDocument;
    public IReadOnlyList<string> PerspectivePlaneNames { get; } = new[] { "Front", "Side", "Ground" };

    private void InitializePerspectiveControls()
    {
        PerspectivePanel.ToolRequested += (_, request) =>
        {
            if (_canvasDragMode != CanvasDragMode.None) return;
            HideToolDesignDrawer();
            if (request.Tool == 1) ViewModel.UsePerspectiveLineTool(); else ViewModel.UsePerspectiveRectangleTool(request.Plane);
            ShowPerspectiveGuides(false); CanvasScrollViewer.Focus(FocusState.Programmatic);
        };
        PerspectivePanel.RegisterPropertyChangedCallback(PerspectiveGuidesPanel.PointEditingEnabledProperty, (_, _) => UpdatePerspectiveControls());
    }

    private void ShowPerspectiveGuides(bool editPoints = true)
    {
        if (_focusWorkspace) ToggleWorkspaceFocus();
        CanvasGridSettingsFlyout.Hide(); ExpandRightControlsPane(); _guidesInspectorOpen = true;
        PerspectivePanel.PointEditingEnabled = editPoints;
        UpdatePerspectiveControls();
    }

    private void PerspectiveGuidesTab_Click(object sender, RoutedEventArgs args) => ShowPerspectiveGuides();
    private void PerspectivePropertiesTab_Click(object sender, RoutedEventArgs args)
    {
        _guidesInspectorOpen = false; PerspectiveHandles.CancelEdit(); UpdatePerspectiveControls(); UpdateInspectorViewport();
    }
    private void OpenPerspectiveGuides_Click(object sender, RoutedEventArgs args) => ShowPerspectiveGuides();
    private void CanvasGridSettingsButton_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.CanvasGridMode != 0) ShowPerspectiveGuides();
        else if (sender is FrameworkElement anchor) CanvasGridSettingsFlyout.ShowAt(anchor);
    }

    private void PerspectivePlane_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ShouldIgnoreControlEvent() || sender is not ComboBox { SelectedIndex: >= 0 } combo || !ViewModel.IsPerspectiveRectangleToolActive
            || combo.SelectedIndex == ViewModel.PerspectiveRectanglePlaneIndex) return;
        ViewModel.UsePerspectiveRectangleTool((SnapStudio.Core.Documents.PerspectiveRectanglePlane)combo.SelectedIndex);
    }

    private void UpdatePerspectiveControls()
    {
        if (PerspectivePanel is null || PerspectiveHandles is null) return;
        int tool = ViewModel.ActivePerspectiveToolIndex;
        if (tool > 0 && tool != _lastPerspectiveTool && _isReadyForControlEvents)
        {
            _lastPerspectiveTool = tool; ShowPerspectiveGuides(false);
        }
        _lastPerspectiveTool = tool;
        if (!Equals(_guideDocument, ViewModel.CurrentDocumentId)) { PerspectiveHandles.CancelEdit(); _guideDocument = ViewModel.CurrentDocumentId; }
        InspectorBodyGrid.Visibility = _guidesInspectorOpen ? Visibility.Collapsed : Visibility.Visible;
        PerspectiveGuidesScroll.Visibility = _guidesInspectorOpen ? Visibility.Visible : Visibility.Collapsed;
        PerspectivePropertiesTab.IsChecked = !_guidesInspectorOpen; PerspectiveGuidesTab.IsChecked = _guidesInspectorOpen;
        bool handles = _guidesInspectorOpen && _rightControlsPaneIsExpanded && !_focusWorkspace && ViewModel.HasCurrentDocument
            && ViewModel.ShowCanvasGrid && ViewModel.CanvasGridMode != 0 && PerspectivePanel.PointEditingEnabled;
        if (!handles) PerspectiveHandles.CancelEdit();
        PerspectiveHandles.Visibility = handles ? Visibility.Visible : Visibility.Collapsed;
        PerspectiveToolOptions.Visibility = tool > 0 ? Visibility.Visible : Visibility.Collapsed;
        CanvasToolHint.Visibility = tool > 0 ? Visibility.Collapsed : Visibility.Visible;
        PerspectivePlaneControl.Visibility = tool == 2 ? Visibility.Visible : Visibility.Collapsed;
        bool room = PerspectiveToolOptions.Parent is not FrameworkElement row || row.ActualWidth >= 610;
        PerspectiveStrokeColor.Visibility = PerspectiveStrokeWidth.Visibility = PerspectiveStrokeUnit.Visibility = room ? Visibility.Visible : Visibility.Collapsed;
    }
}
