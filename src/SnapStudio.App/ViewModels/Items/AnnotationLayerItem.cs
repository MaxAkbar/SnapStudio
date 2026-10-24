using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
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

    private string ColorLabelText => layer.CustomColor is ColorRgba color
        ? $"Custom #{color.R:X2}{color.G:X2}{color.B:X2}" : layer.ColorLabel.ToString();

    public string SelectionHelpText => $"{Name}, {ObjectCount}. {(isActive ? "Selected layer." : "Select this layer to add objects here.")} Color label: {ColorLabelText}. Right-click for layer options.";

    public Visibility IndicatorVisibility => isActive || layer.ColorLabel != LayerColorLabel.None || layer.CustomColor is not null
        ? Visibility.Visible : Visibility.Collapsed;

    public bool CanMoveUp => canMoveUp;

    public bool CanMoveDown => canMoveDown;

    public string ColorMenuText => $"Color label: {ColorLabelText}";

    public Brush IndicatorBrush => new SolidColorBrush(IndicatorColor);

    public Color IndicatorColor => layer.CustomColor is ColorRgba custom
        ? Color.FromArgb(255, custom.R, custom.G, custom.B) : layer.ColorLabel switch
        {
            LayerColorLabel.Red => Color.FromArgb(255, 239, 112, 112),
            LayerColorLabel.Orange => Color.FromArgb(255, 245, 169, 91),
            LayerColorLabel.Yellow => Color.FromArgb(255, 240, 208, 93),
            LayerColorLabel.Green => Color.FromArgb(255, 113, 202, 150),
            LayerColorLabel.Blue => Color.FromArgb(255, 105, 174, 245),
            LayerColorLabel.Purple => Color.FromArgb(255, 186, 143, 235),
            LayerColorLabel.Gray => Color.FromArgb(255, 169, 178, 190),
            _ => Color.FromArgb(255, 76, 194, 255)
        };

    public Visibility HiddenVisibility => layer.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    public bool IsExpanded => isExpanded;

    public string VisibilityAutomationName => layer.IsVisible ? $"Hide {Name} layer" : $"Show {Name} layer";

    public string MoveAutomationName => $"Move selected object to {Name}";

    public bool CanMoveSelectedHere => canMoveSelectedHere && layer.IsVisible;

    public Visibility MoveVisibility => CanMoveSelectedHere
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Brush BackgroundBrush => isActive
        ? new SolidColorBrush(Color.FromArgb(255, 46, 65, 81))
        : new SolidColorBrush(Color.FromArgb(255, 37, 40, 48));

    public Brush BorderBrush => isActive
        ? new SolidColorBrush(Color.FromArgb(255, 83, 110, 133))
        : new SolidColorBrush(Color.FromArgb(255, 55, 63, 73));
}
