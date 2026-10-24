using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SnapStudio.App.Controls;

// Keep selection state separate from palette brushes so both respond to theme changes.
public sealed class WorkspaceSelectionFrame : Grid
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(WorkspaceSelectionFrame), new PropertyMetadata(false, Refresh));
    public static readonly DependencyProperty NormalBackgroundProperty = RegisterBrush(nameof(NormalBackground));
    public static readonly DependencyProperty SelectedBackgroundProperty = RegisterBrush(nameof(SelectedBackground));
    public static readonly DependencyProperty NormalBorderBrushProperty = RegisterBrush(nameof(NormalBorderBrush));
    public static readonly DependencyProperty SelectedBorderBrushProperty = RegisterBrush(nameof(SelectedBorderBrush));

    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public Brush? NormalBackground { get => (Brush?)GetValue(NormalBackgroundProperty); set => SetValue(NormalBackgroundProperty, value); }
    public Brush? SelectedBackground { get => (Brush?)GetValue(SelectedBackgroundProperty); set => SetValue(SelectedBackgroundProperty, value); }
    public Brush? NormalBorderBrush { get => (Brush?)GetValue(NormalBorderBrushProperty); set => SetValue(NormalBorderBrushProperty, value); }
    public Brush? SelectedBorderBrush { get => (Brush?)GetValue(SelectedBorderBrushProperty); set => SetValue(SelectedBorderBrushProperty, value); }

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(WorkspaceSelectionFrame), new PropertyMetadata(null, Refresh));

    private static void Refresh(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var border = (WorkspaceSelectionFrame)sender;
        border.Background = border.IsSelected ? border.SelectedBackground : border.NormalBackground;
        border.BorderBrush = border.IsSelected ? border.SelectedBorderBrush : border.NormalBorderBrush;
    }
}
