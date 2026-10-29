using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.ViewModels.Items;

public enum LayerTreeRowKind { Layer, Folder, ObjectGroup, Object }

public sealed class LayerTreeRow : INotifyPropertyChanged
{
    private sealed record State(string Name, string Detail, int Depth, bool IsSelected, bool IsExpanded, bool IsVisible,
        string ObjectKind, Visibility IndicatorVisibility, AnnotationLayerItem? Layer, AnnotationOverlayItem? Object);
    private State _state;

    public LayerTreeRow(Guid Id, LayerTreeRowKind Kind, string Name, string Detail, int Depth,
        bool IsSelected, bool IsExpanded, bool IsVisible, AnnotationLayerItem? Layer = null, AnnotationOverlayItem? Object = null)
    {
        this.Id = Id; this.Kind = Kind;
        _state = new(Name, Detail, Depth, IsSelected, IsExpanded, IsVisible, Object?.Kind.ToString() ?? "Rectangle",
            Layer?.IndicatorVisibility ?? Visibility.Collapsed, Layer, Object);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public LayerTreeRowKind Kind { get; }
    public string Name => _state.Name;
    public string Detail => _state.Detail;
    public int Depth => _state.Depth;
    public bool IsSelected => _state.IsSelected;
    public bool IsExpanded => _state.IsExpanded;
    public bool IsVisible => _state.IsVisible;
    public AnnotationLayerItem? Layer => _state.Layer;
    public AnnotationOverlayItem? Object => _state.Object;

    internal void UpdateFrom(LayerTreeRow next)
    {
        if (Key != next.Key) throw new InvalidOperationException("A retained tree row must keep its identity.");
        if (_state == next._state) return;
        _state = next._state;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public override string ToString() => Name;
    public string Key => Kind + ":" + Id;
    public Thickness Indent => new(Depth * 12, 0, 0, 3);
    public bool CanExpand => Kind != LayerTreeRowKind.Object;
    public string Chevron => IsExpanded ? "\uE70D" : "\uE76C";
    public Visibility ChevronVisibility => CanExpand ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ObjectIconVisibility => Kind == LayerTreeRowKind.Object ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContainerIconVisibility => Kind == LayerTreeRowKind.Object ? Visibility.Collapsed : Visibility.Visible;
    public string Icon => Kind == LayerTreeRowKind.Layer ? "Library" : "Folder";
    public string ObjectKind => _state.ObjectKind;
    public Brush? IndicatorBrush => Layer?.IndicatorBrush;
    public Visibility IndicatorVisibility => _state.IndicatorVisibility;
    public string AutomationName => $"Select {Name} {Kind switch { LayerTreeRowKind.Folder => "folder", LayerTreeRowKind.ObjectGroup => "object group", LayerTreeRowKind.Object => "object", _ => "layer" }}";
    public string ExpandName => (IsExpanded ? "Collapse " : "Expand ") + Name;
    public string VisibilityName => (IsVisible ? "Hide " : "Show ") + Name;
    public Visibility HiddenVisibility => IsVisible ? Visibility.Collapsed : Visibility.Visible;
    public string Help => "Ctrl-click to toggle selection; Shift-click selects a range. Right-click for grouping, merge, cut, or move.";
}
