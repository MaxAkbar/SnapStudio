using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;
using Windows.UI;

namespace SnapStudio.App.ViewModels.Items;

public sealed class AnnotationLayerItem(
    AnnotationLayer layer,
    IReadOnlyList<AnnotationOverlayItem> objects,
    bool isActive,
    bool canMoveSelectedHere,
    bool isExpanded)
{
    public Guid Id => layer.Id;

    public string Name => layer.Name;

    public IReadOnlyList<AnnotationOverlayItem> Objects => objects;

    public string ObjectCount => $"{objects.Count} object{(objects.Count == 1 ? string.Empty : "s")}";

    public string SelectionAutomationName => $"Select {Name} layer";

    public string TreeAutomationName => $"{Name}, {ObjectCount}";

    public bool IsExpanded => isExpanded;

    public string VisibilityAutomationName => layer.IsVisible ? $"Hide {Name} layer" : $"Show {Name} layer";

    public string VisibilityText => layer.IsVisible ? "Hide" : "Show";

    public string MoveAutomationName => $"Move selected object to {Name}";

    public bool CanMoveSelectedHere => canMoveSelectedHere && layer.IsVisible;

    public Visibility MoveVisibility => CanMoveSelectedHere
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Brush BackgroundBrush => isActive
        ? new SolidColorBrush(Color.FromArgb(255, 30, 48, 61))
        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
}
