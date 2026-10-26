using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SnapStudio.Core.Documents;
using Windows.Foundation;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private MenuFlyout CreateCanvasToolMenu(Point position)
    {
        var menu = new MenuFlyout();
        string group = Guid.NewGuid().ToString();
        var documentId = ViewModel.CurrentDocumentId;
        (string name, int? index)[] tools =
        [
            ("Move", -1), ("Marquee", null), ("Rectangle", 0), ("Ellipse", 1), ("Line", 2), ("Arrow", 3),
            ("Text", 4), ("Pen", 7), ("Highlight", 5), ("Blur", 6), ("Fill bucket", 8), ("Custom shapes", 9)
        ];
        foreach ((string name, int? index) in tools)
        {
            if (index is null)
            {
                var selections = new MenuFlyoutSubItem { Text = "Selection tools" };
                PopulateAreaSelectionTools(selections.Items);
                menu.Items.Add(selections);
                continue;
            }
            if (index == 0) menu.Items.Add(new MenuFlyoutSeparator());
            var item = new RadioMenuFlyoutItem
            {
                Text = name,
                GroupName = group,
                KeyboardAcceleratorTextOverride = ToolShortcut(index),
                IsChecked = index == -1 ? ViewModel.IsMoveToolActive : index.HasValue ? !ViewModel.IsSelectionToolActive && !ViewModel.IsStampToolActive && !ViewModel.IsPerspectiveRectangleToolActive && !ViewModel.IsPerspectiveLineToolActive && ViewModel.AnnotationToolIndex == index
                    : ViewModel.IsMarqueeToolActive
            };
            item.Click += (_, _) =>
            {
                if (ViewModel.CurrentDocumentId == documentId && ViewModel.HasCurrentDocument && _canvasDragMode == CanvasDragMode.None)
                    SelectCanvasTool(index);
            };
            menu.Items.Add(item);
            if (index == 2)
            {
                var perspective = new RadioMenuFlyoutItem { Text = "Perspective line", GroupName = group, IsChecked = ViewModel.IsPerspectiveLineToolActive };
                perspective.Click += (_, _) => { if (ViewModel.CurrentDocumentId != documentId || _canvasDragMode != CanvasDragMode.None) return; HideToolDesignDrawer(); _objectContextMenu?.Hide(); ViewModel.UsePerspectiveLineTool(); PropertiesSectionExpander.IsExpanded = true; UpdateBindingsFromViewModel(); CanvasScrollViewer.Focus(FocusState.Programmatic); };
                menu.Items.Add(perspective);
            }
            if (index == 0)
            {
                var perspective = new MenuFlyoutSubItem { Text = "Perspective rectangle" };
                foreach (var plane in Enum.GetValues<PerspectiveRectanglePlane>())
                {
                    var choice = new RadioMenuFlyoutItem { Text = plane + " plane", GroupName = group, IsChecked = ViewModel.IsPerspectiveRectangleToolActive && ViewModel.RectanglePerspectivePlane == plane };
                    choice.Click += (_, _) => { if (ViewModel.CurrentDocumentId != documentId || _canvasDragMode != CanvasDragMode.None) return; HideToolDesignDrawer(); _objectContextMenu?.Hide(); ViewModel.UsePerspectiveRectangleTool(plane); UpdateBindingsFromViewModel(); CanvasScrollViewer.Focus(FocusState.Programmatic); };
                    perspective.Items.Add(choice);
                }
                menu.Items.Add(perspective);
            }
        }
        var stamps = new RadioMenuFlyoutItem
        {
            Text = "Stamps…",
            GroupName = group,
            KeyboardAcceleratorTextOverride = "S",
            IsChecked = ViewModel.IsStampToolActive
        };
        stamps.Click += (_, _) => { _objectContextMenu?.Hide(); ShowStampPicker(); };
        menu.Items.Add(stamps);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateGridSettingsMenuItem(position));
        return menu;
    }

    private void ShowCanvasToolMenu(Point position)
    {
        CanvasScrollViewer.Focus(FocusState.Programmatic);
        _objectContextMenu?.Hide();
        MenuFlyout menu = CreateCanvasToolMenu(position);
        menu.Closed += (_, _) => { if (ReferenceEquals(_objectContextMenu, menu)) _objectContextMenu = null; };
        _objectContextMenu = menu;
        menu.ShowAt(this, new FlyoutShowOptions { Position = position });
    }

    private void SelectCanvasTool(int? toolIndex)
    {
        CancelPolygonSelection();
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
