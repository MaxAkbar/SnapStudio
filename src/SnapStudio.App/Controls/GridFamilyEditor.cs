using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Rendering;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class GridFamilyEditor : StackPanel
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(GridGuideSettings), typeof(GridFamilyEditor),
        new PropertyMetadata(null, (sender, _) => ((GridFamilyEditor)sender).Refresh()));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(GridFamilyEditor),
        new PropertyMetadata("Base grid", (sender, _) => ((GridFamilyEditor)sender).Refresh()));
    public static readonly DependencyProperty DensityVisibilityProperty = DependencyProperty.Register(nameof(DensityVisibility), typeof(Visibility), typeof(GridFamilyEditor),
        new PropertyMetadata(Visibility.Visible, (sender, _) => ((GridFamilyEditor)sender).Refresh()));
    public GridGuideSettings? Settings { get => (GridGuideSettings?)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Visibility DensityVisibility { get => (Visibility)GetValue(DensityVisibilityProperty); set => SetValue(DensityVisibilityProperty, value); }

    private readonly TextBlock _heading = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly Border _swatch = new() { Width = 18, Height = 18, CornerRadius = new(3) };
    private readonly TextBlock _hex = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
    private readonly Button _choose = new();
    private readonly ColorPicker _picker = new() { IsAlphaEnabled = false };
    private readonly NumberBox _opacity = new() { Header = "Opacity (%)", Minimum = 0, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly Slider _opacitySlider = new() { Minimum = 0, Maximum = 100, StepFrequency = 1 };
    private readonly NumberBox _density = new() { Header = "Density", Minimum = 4, Maximum = 32, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly Slider _densitySlider = new() { Minimum = 4, Maximum = 32, StepFrequency = 1 };
    private readonly StackPanel _densityPanel = new();
    private bool _refreshing;

    public GridFamilyEditor()
    {
        Spacing = 4; Margin = new(0, 4, 0, 8);
        var heading = new Grid { ColumnSpacing = 8 };
        heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        label.Children.Add(_swatch); label.Children.Add(_hex); _choose.Content = label;
        _choose.Flyout = new Flyout { Content = _picker };
        heading.Children.Add(_heading); Grid.SetColumn(_choose, 1); heading.Children.Add(_choose); Children.Add(heading);
        var fields = new Grid { ColumnSpacing = 8 };
        fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new());
        var opacity = new StackPanel(); opacity.Children.Add(_opacity); opacity.Children.Add(_opacitySlider);
        _densityPanel.Children.Add(_density); _densityPanel.Children.Add(_densitySlider);
        fields.Children.Add(opacity); Grid.SetColumn(_densityPanel, 1); fields.Children.Add(_densityPanel); Children.Add(fields);
        _picker.ColorChanged += (_, args) => Change(style => style with { Color = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}" });
        _opacity.ValueChanged += (_, args) => Change(style => style with { Opacity = args.NewValue });
        _opacitySlider.ValueChanged += (_, args) => Change(style => style with { Opacity = args.NewValue });
        _density.ValueChanged += (_, args) => Change(style => style with { Density = double.IsFinite(args.NewValue) ? (int)Math.Round(args.NewValue) : style.Density });
        _densitySlider.ValueChanged += (_, args) => Change(style => style with { Density = (int)Math.Round(args.NewValue) });
        Refresh();
    }

    private void Change(Func<GridGuideSettings, GridGuideSettings> change)
    {
        if (_refreshing || Settings is not { } settings) return;
        var next = change(settings);
        next = next with { Opacity = double.IsFinite(next.Opacity) ? Math.Clamp(next.Opacity, 0, 100) : settings.Opacity, Density = Math.Clamp(next.Density, 4, 32) };
        if (next != settings) Settings = next; else Refresh();
    }

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var settings = Settings ?? new("#64748B", 45);
            var value = settings.GetColor(); var color = Color.FromArgb(255, value.R, value.G, value.B);
            _heading.Text = Title; _hex.Text = settings.Color; _swatch.Background = new SolidColorBrush(color); _picker.Color = color;
            _opacity.Value = _opacitySlider.Value = settings.Opacity; _density.Value = _densitySlider.Value = settings.Density;
            _densityPanel.Visibility = DensityVisibility;
            AutomationProperties.SetName(_choose, Title + " color"); AutomationProperties.SetName(_picker, Title + " color picker");
            AutomationProperties.SetName(_opacity, Title + " opacity percent"); AutomationProperties.SetName(_opacitySlider, Title + " opacity slider");
            AutomationProperties.SetName(_density, Title + " density"); AutomationProperties.SetName(_densitySlider, Title + " density slider");
        }
        finally { _refreshing = false; }
    }
}
