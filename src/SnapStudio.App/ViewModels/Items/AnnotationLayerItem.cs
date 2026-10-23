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
    bool isExpanded,
    bool canMoveUp,
    bool canMoveDown)
{
    public Guid Id => layer.Id;

    public string Name => layer.Name;

    public IReadOnlyList<AnnotationOverlayItem> Objects => objects;

    public string ObjectCount => $"{objects.Count} object{(objects.Count == 1 ? string.Empty : "s")}";

    public string SelectionAutomationName => $"Select {Name} layer";

    public string TreeAutomationName => $"{Name}, {ObjectCount}";

    public string SelectionHelpText => $"{Name}, {ObjectCount}. {(isActive ? "Selected layer." : "Select this layer to add objects here.")} Color label: {layer.ColorLabel}. Right-click for layer options.";

    public Visibility IndicatorVisibility => isActive || layer.ColorLabel != LayerColorLabel.None
        ? Visibility.Visible : Visibility.Collapsed;

    public bool CanMoveUp => canMoveUp;

    public bool CanMoveDown => canMoveDown;

    public string ColorMenuText => $"Color label: {layer.ColorLabel}";

    public Brush IndicatorBrush => new SolidColorBrush(layer.ColorLabel switch
    {
        LayerColorLabel.Red => Color.FromArgb(255, 239, 112, 112),
        LayerColorLabel.Orange => Color.FromArgb(255, 245, 169, 91),
        LayerColorLabel.Yellow => Color.FromArgb(255, 240, 208, 93),
        LayerColorLabel.Green => Color.FromArgb(255, 113, 202, 150),
        LayerColorLabel.Blue => Color.FromArgb(255, 105, 174, 245),
        LayerColorLabel.Purple => Color.FromArgb(255, 186, 143, 235),
        LayerColorLabel.Gray => Color.FromArgb(255, 169, 178, 190),
        _ => Color.FromArgb(255, 76, 194, 255)
    });

    public Visibility HiddenVisibility => layer.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    public bool IsExpanded => isExpanded;

    public string VisibilityAutomationName => layer.IsVisible ? $"Hide {Name} layer" : $"Show {Name} layer";

    public string MoveAutomationName => $"Move selected object to {Name}";

    public bool CanMoveSelectedHere => canMoveSelectedHere && layer.IsVisible;

    public Visibility MoveVisibility => CanMoveSelectedHere
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Brush BackgroundBrush => isActive
        ? new SolidColorBrush(Color.FromArgb(255, 36, 68, 94))
        : new SolidColorBrush(Color.FromArgb(255, 32, 38, 47));

    public Brush BorderBrush => isActive
        ? new SolidColorBrush(Color.FromArgb(255, 76, 194, 255))
        : new SolidColorBrush(Color.FromArgb(255, 48, 59, 71));
}
