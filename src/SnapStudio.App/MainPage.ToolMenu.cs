using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private MenuFlyout CreateCanvasToolMenu()
    {
        var menu = new MenuFlyout();
        string group = Guid.NewGuid().ToString();
        var documentId = ViewModel.CurrentDocumentId;
        (string name, int? index)[] tools =
        [
            ("Move", -1), ("Marquee", null), ("Rectangle", 0), ("Ellipse", 1), ("Line", 2), ("Arrow", 3),
            ("Text", 4), ("Pen", 7), ("Highlight", 5), ("Blur", 6), ("Fill bucket", 8)
        ];
        foreach ((string name, int? index) in tools)
        {
            if (index == 0) menu.Items.Add(new MenuFlyoutSeparator());
            var item = new RadioMenuFlyoutItem
            {
                Text = name, GroupName = group,
                KeyboardAcceleratorTextOverride = ToolShortcut(index),
                IsChecked = index == -1 ? ViewModel.IsMoveToolActive : index.HasValue ? !ViewModel.IsSelectionToolActive && ViewModel.AnnotationToolIndex == index
                    : ViewModel.IsMarqueeToolActive
            };
            item.Click += (_, _) =>
            {
                if (ViewModel.CurrentDocumentId == documentId && ViewModel.HasCurrentDocument && _canvasDragMode == CanvasDragMode.None)
                    SelectCanvasTool(index);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ShowCanvasToolMenu(Point position)
    {
        CanvasScrollViewer.Focus(FocusState.Programmatic);
        _objectContextMenu?.Hide();
        MenuFlyout menu = CreateCanvasToolMenu();
        menu.Closed += (_, _) => { if (ReferenceEquals(_objectContextMenu, menu)) _objectContextMenu = null; };
        _objectContextMenu = menu;
        menu.ShowAt(this, new FlyoutShowOptions { Position = position });
    }

    private void SelectCanvasTool(int? toolIndex)
    {
        HideToolDesignDrawer();
        _objectContextMenu?.Hide();
        if (toolIndex == -1) ViewModel.UseMoveTool();
        else if (toolIndex is int index) ViewModel.UpdateAnnotationTool(index);
        else ViewModel.UseSelectionTool();
        PropertiesSectionExpander.IsExpanded = true;
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
        CanvasScrollViewer.Focus(FocusState.Programmatic);
    }
}
