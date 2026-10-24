using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.UI;

namespace SnapStudio.App.ViewModels.Items;

public sealed class AnnotationLayerItem : INotifyPropertyChanged
{
    private sealed record State(string Name, bool IsVisible, LayerColorLabel ColorLabel, ColorRgba? CustomColor,
        bool IsActive, bool CanMoveSelectedHere, bool IsExpanded, bool CanMoveUp, bool CanMoveDown);
    private State _state = new(string.Empty, true, LayerColorLabel.None, null, false, false, true, false, false);
    private readonly SolidColorBrush _indicatorBrush = new();

    public AnnotationLayerItem(AnnotationLayer layer) => Id = layer.Id;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public string Name => _state.Name;
    public ObservableCollection<AnnotationOverlayItem> Objects { get; } = [];

    internal void Update(AnnotationLayer layer, IReadOnlyList<AnnotationOverlayItem> objects, bool isActive,
        bool canMoveSelectedHere, bool isExpanded, bool canMoveUp, bool canMoveDown)
    {
        var next = new State(layer.Name, layer.IsVisible, layer.ColorLabel, layer.CustomColor,
            isActive, canMoveSelectedHere, isExpanded, canMoveUp, canMoveDown);
        bool changed = next != _state || Objects.Count != objects.Count;
        _state = next;
        CollectionUpdates.Synchronize(Objects, objects);
        _indicatorBrush.Color = IndicatorColor;
        if (changed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string ObjectCount => $"{Objects.Count} object{(Objects.Count == 1 ? string.Empty : "s")}";

    public string SelectionAutomationName => $"Select {Name} layer";

    public string TreeAutomationName => $"{Name}, {ObjectCount}";

    private string ColorLabelText => _state.CustomColor is ColorRgba color
        ? $"Custom #{color.R:X2}{color.G:X2}{color.B:X2}" : _state.ColorLabel.ToString();

    public string SelectionHelpText => $"{Name}, {ObjectCount}. {(IsActive ? "Selected layer." : "Select this layer to add objects here.")} Color label: {ColorLabelText}. Right-click for layer options.";

    public Visibility IndicatorVisibility => IsActive || _state.ColorLabel != LayerColorLabel.None || _state.CustomColor is not null
        ? Visibility.Visible : Visibility.Collapsed;

    public bool CanMoveUp => _state.CanMoveUp;

    public bool CanMoveDown => _state.CanMoveDown;

    public string ColorMenuText => $"Color label: {ColorLabelText}";

    public Brush IndicatorBrush => _indicatorBrush;

    public Color IndicatorColor => _state.CustomColor is ColorRgba custom
        ? Color.FromArgb(255, custom.R, custom.G, custom.B) : _state.ColorLabel switch
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

    public Visibility HiddenVisibility => _state.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    public bool IsExpanded => _state.IsExpanded;

    public bool IsActive => _state.IsActive;

    public string VisibilityAutomationName => _state.IsVisible ? $"Hide {Name} layer" : $"Show {Name} layer";

    public string MoveAutomationName => $"Move selected object to {Name}";

    public bool CanMoveSelectedHere => _state.CanMoveSelectedHere && _state.IsVisible;

    public Visibility MoveVisibility => CanMoveSelectedHere
        ? Visibility.Visible
        : Visibility.Collapsed;

}
