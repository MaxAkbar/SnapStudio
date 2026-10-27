using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Rendering;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed record PerspectiveToolRequest(int Tool, PerspectiveRectanglePlane Plane);

public sealed class PerspectiveGuidesPanel : StackPanel
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(PerspectiveGuidesPanel),
        new PropertyMetadata(value, (sender, _) => ((PerspectiveGuidesPanel)sender).Refresh()));
    public static readonly DependencyProperty PerspectiveProperty = Property(nameof(Perspective), typeof(PerspectiveGridSettings), null);
    public static readonly DependencyProperty FamiliesProperty = Property(nameof(Families), typeof(CanvasGridFamilies), null);
    public static readonly DependencyProperty ShowGridProperty = Property(nameof(ShowGrid), typeof(bool), false);
    public static readonly DependencyProperty SnapEnabledProperty = Property(nameof(SnapEnabled), typeof(bool), true);
    public static readonly DependencyProperty CellSizeProperty = Property(nameof(CellSize), typeof(double), 32d);
    public static readonly DependencyProperty SelectedFamilyProperty = Property(nameof(SelectedFamily), typeof(GridGuideFamily), GridGuideFamily.Left);
    public static readonly DependencyProperty PointEditingEnabledProperty = Property(nameof(PointEditingEnabled), typeof(bool), true);
    public static readonly DependencyProperty ToolIndexProperty = Property(nameof(ToolIndex), typeof(int), 0);
    public static readonly DependencyProperty PlaneProperty = Property(nameof(Plane), typeof(PerspectiveRectanglePlane), PerspectiveRectanglePlane.Side);
    public PerspectiveGridSettings? Perspective { get => (PerspectiveGridSettings?)GetValue(PerspectiveProperty); set => SetValue(PerspectiveProperty, value); }
    public CanvasGridFamilies? Families { get => (CanvasGridFamilies?)GetValue(FamiliesProperty); set => SetValue(FamiliesProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public bool SnapEnabled { get => (bool)GetValue(SnapEnabledProperty); set => SetValue(SnapEnabledProperty, value); }
    public double CellSize { get => (double)GetValue(CellSizeProperty); set => SetValue(CellSizeProperty, value); }
    public GridGuideFamily SelectedFamily { get => (GridGuideFamily)GetValue(SelectedFamilyProperty); set => SetValue(SelectedFamilyProperty, value); }
    public bool PointEditingEnabled { get => (bool)GetValue(PointEditingEnabledProperty); set => SetValue(PointEditingEnabledProperty, value); }
    public int ToolIndex { get => (int)GetValue(ToolIndexProperty); set => SetValue(ToolIndexProperty, value); }
    public PerspectiveRectanglePlane Plane { get => (PerspectiveRectanglePlane)GetValue(PlaneProperty); set => SetValue(PlaneProperty, value); }
    public event EventHandler<PerspectiveToolRequest>? ToolRequested;

    private readonly ToggleSwitch _show = new();
    private readonly ToggleButton[] _modes = new ToggleButton[6];
    private readonly Dictionary<GridGuideFamily, (Grid Row, Button Select, Border Dot, TextBlock Name, TextBlock Summary, Button Eye)> _rows = [];
    private readonly Dictionary<GridGuideFamily, double> _previousOpacity = [];
    private readonly GridFamilyEditor _editor = new();
    private readonly NumericSetting _horizon = new("Horizon Y (%)", -300, 400), _radius = new("Curve radius (%)", 10, 300), _spacing = new("Cell spacing (image px)", 4, 512);
    private readonly NumericSetting _positionX = new("VP X (%)", -500, 500), _positionY = new("VP Y (%)", -500, 500);
    private readonly TextBlock _linked = new() { Text = "This point follows the shared curve radius.", FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _editPoints = new() { Content = "Edit points on canvas" }, _snap = new() { Content = "Snap to visible guides" };
    private readonly ToggleButton _line = new(), _rectangle = new();
    private readonly ComboBox _plane = new() { Header = "Rectangle plane", ItemsSource = new[] { "Front", "Side", "Ground" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _refreshing;
    private CanvasGridMode _lastMode;

    public PerspectiveGuidesPanel()
    {
        Spacing = 10;
        var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 4 }; title.Children.Add(new TextBlock { Text = "Grid & perspective", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        title.Children.Add(new TextBlock { Text = "Editing guides. Never exported.", FontSize = 10 }); heading.Children.Add(title);
        _show.MinWidth = 0; AutomationProperties.SetName(_show, "Show guides"); Grid.SetColumn(_show, 1); heading.Children.Add(_show); Children.Add(heading);
        var modes = new Grid { ColumnSpacing = 3 };
        string[] names = ["Rectangular", "1-point perspective", "2-point perspective", "3-point perspective", "4-point curved", "5-point fisheye"];
        for (int mode = 0; mode < 6; mode++)
        {
            modes.ColumnDefinitions.Add(new()); var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new WorkspaceIcon { Kind = "Grid" + mode, Width = 21, Height = 21, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = mode == 0 ? "Grid" : mode + " pt", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new ToggleButton { Content = stack, Padding = new(2, 5, 2, 5), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, names[mode]); ToolTipService.SetToolTip(button, names[mode]); int choice = mode;
            button.Click += (_, _) => { if (_refreshing) return; Perspective = Current with { Mode = (CanvasGridMode)choice }; ShowGrid = true; };
            _modes[mode] = button; Grid.SetColumn(button, mode); modes.Children.Add(button);
        }
        Children.Add(modes); Children.Add(_horizon); Children.Add(_radius); Children.Add(_spacing);
        var familyHeading = new Grid(); familyHeading.ColumnDefinitions.Add(new()); familyHeading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        familyHeading.Children.Add(new TextBlock { Text = "Guide families", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        var caption = new TextBlock { Text = "Opacity   Density", FontSize = 9, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(caption, 1); familyHeading.Children.Add(caption); Children.Add(familyHeading);
        var rows = new StackPanel { Spacing = 2 };
        foreach (GridGuideFamily family in Enum.GetValues<GridGuideFamily>())
        {
            var row = new Grid { ColumnSpacing = 2 }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var content = new Grid { ColumnSpacing = 6 }; content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var dot = new Border { Width = 9, Height = 9, CornerRadius = new(5), VerticalAlignment = VerticalAlignment.Center }; var name = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var summary = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(dot); Grid.SetColumn(name, 1); content.Children.Add(name); Grid.SetColumn(summary, 2); content.Children.Add(summary);
            var select = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(6, 4, 6, 4), MinWidth = 0 };
            select.Click += (_, _) => SelectedFamily = family;
            var eye = new Button { Content = new WorkspaceIcon { Kind = "Eye", Width = 13, Height = 13 }, Padding = new(4), MinWidth = 24, MinHeight = 28 };
            eye.Click += (_, _) => { var style = Styles.Get(family); double opacity = style.Opacity; if (opacity > 0) _previousOpacity[family] = opacity; SetFamily(family, style with { Opacity = opacity > 0 ? 0 : _previousOpacity.GetValueOrDefault(family, 75) }); };
            row.Children.Add(select); Grid.SetColumn(eye, 1); row.Children.Add(eye); rows.Children.Add(row); _rows.Add(family, (row, select, dot, name, summary, eye));
        }
        Children.Add(rows);
        var editorContent = new StackPanel { Spacing = 7 }; editorContent.Children.Add(_editor); editorContent.Children.Add(_positionX); editorContent.Children.Add(_positionY); editorContent.Children.Add(_linked);
        var border = new Border { Padding = new(9), BorderThickness = new(1), CornerRadius = new(6), Child = editorContent };
        border.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)); border.Background = new SolidColorBrush(Color.FromArgb(12, 128, 128, 128)); Children.Add(border);
        _editPoints.MinHeight = 28; Children.Add(_editPoints);
        var toolRows = new Grid { ColumnSpacing = 5 }; toolRows.ColumnDefinitions.Add(new()); toolRows.ColumnDefinitions.Add(new());
        _line.Content = ToolContent("Line", "PerspectiveLine"); _rectangle.Content = ToolContent("Rectangle", "PerspectiveRectangle");
        foreach (var button in new[] { _line, _rectangle }) { button.MinWidth = 0; button.Padding = new(5); button.HorizontalAlignment = HorizontalAlignment.Stretch; }
        AutomationProperties.SetName(_line, "Use perspective line"); AutomationProperties.SetName(_rectangle, "Use perspective rectangle");
        toolRows.Children.Add(_line); Grid.SetColumn(_rectangle, 1); toolRows.Children.Add(_rectangle); Children.Add(new TextBlock { Text = "Perspective tools", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 }); Children.Add(toolRows); Children.Add(_plane);
        var more = new StackPanel { Spacing = 5 }; more.Children.Add(_snap);
        var resetPositions = new Button { Content = "Reset perspective positions", HorizontalAlignment = HorizontalAlignment.Stretch };
        resetPositions.Click += (_, _) => Perspective = new(Mode: Current.Mode); more.Children.Add(resetPositions);
        var resetFamilies = new Button { Content = "Reset guide families", HorizontalAlignment = HorizontalAlignment.Stretch };
        resetFamilies.Click += (_, _) => { _previousOpacity.Clear(); Families = new CanvasGridFamilies().Normalize(); }; more.Children.Add(resetFamilies);
        more.Children.Add(new TextBlock { Text = "Hold Alt to bypass snapping. Off-canvas points appear at the image edges; drag them or type an exact position.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        Children.Add(new Expander { Header = "More options", Content = more, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0 });
        _show.Toggled += (_, _) => { if (!_refreshing) ShowGrid = _show.IsOn; };
        _editPoints.Checked += (_, _) => { if (!_refreshing) PointEditingEnabled = true; }; _editPoints.Unchecked += (_, _) => { if (!_refreshing) PointEditingEnabled = false; };
        _snap.Checked += (_, _) => { if (!_refreshing) SnapEnabled = true; }; _snap.Unchecked += (_, _) => { if (!_refreshing) SnapEnabled = false; };
        _horizon.Edited += (_, value) => { if (!_refreshing) Perspective = Current with { Horizon = value }; };
        _radius.Edited += (_, value) => { if (!_refreshing) Perspective = Current with { Radius = value }; };
        _spacing.Edited += (_, value) => { if (!_refreshing) CellSize = value; };
        _positionX.Edited += (_, value) => { if (!_refreshing) Perspective = SelectedFamily switch { GridGuideFamily.Left => Current with { Left = value }, GridGuideFamily.Right => Current with { Right = value }, _ => Current with { CenterX = value } }; };
        _positionY.Edited += (_, value) => { if (!_refreshing) Perspective = Current with { Vertical = value }; };
        _editor.RegisterPropertyChangedCallback(GridFamilyEditor.SettingsProperty, (_, _) => { if (!_refreshing && _editor.Settings is { } style) SetFamily(SelectedFamily, style); });
        _line.Click += (_, _) => ChooseTool(1); _rectangle.Click += (_, _) => ChooseTool(2);
        _plane.SelectionChanged += (_, _) => { if (!_refreshing && _plane.SelectedIndex >= 0) { Plane = (PerspectiveRectanglePlane)_plane.SelectedIndex; if (ToolIndex == 2) ChooseTool(2); } };
        Refresh();
    }

    private PerspectiveGridSettings Current => (Perspective ?? new()).Normalize();
    private CanvasGridFamilies Styles => (Families ?? new()).Normalize();
    private void ChooseTool(int index) { PointEditingEnabled = false; ToolRequested?.Invoke(this, new(index, Plane)); }
    private void SetFamily(GridGuideFamily family, GridGuideSettings value) => Families = family switch
    {
        GridGuideFamily.Grid => Styles with { Grid = value }, GridGuideFamily.Center => Styles with { Center = value },
        GridGuideFamily.Left => Styles with { Left = value }, GridGuideFamily.Right => Styles with { Right = value },
        GridGuideFamily.Top => Styles with { Top = value }, _ => Styles with { Bottom = value }
    };
    private static StackPanel ToolContent(string name, string icon)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new WorkspaceIcon { Kind = icon, Width = 19, Height = 19 }); content.Children.Add(new TextBlock { Text = name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }); return content;
    }

    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var s = Current; var styles = Styles; var active = CanvasGridFamilies.Active(s.Mode);
            if (!active.Contains(SelectedFamily) || _lastMode == CanvasGridMode.Rectangular && s.Mode != CanvasGridMode.Rectangular)
                SelectedFamily = active.FirstOrDefault(f => f != GridGuideFamily.Grid);
            _lastMode = s.Mode;
            _show.IsOn = ShowGrid; _snap.IsChecked = SnapEnabled; _snap.IsEnabled = ShowGrid; _editPoints.IsChecked = PointEditingEnabled;
            _editPoints.IsEnabled = ShowGrid && s.Mode != CanvasGridMode.Rectangular;
            for (int i = 0; i < 6; i++) _modes[i].IsChecked = (int)s.Mode == i;
            _horizon.Visibility = s.Mode == CanvasGridMode.Rectangular ? Visibility.Collapsed : Visibility.Visible; _horizon.Value = s.Horizon;
            _radius.Visibility = s.Mode >= CanvasGridMode.FourPoint ? Visibility.Visible : Visibility.Collapsed; _radius.Value = s.Radius;
            _spacing.Visibility = s.Mode == CanvasGridMode.Rectangular ? Visibility.Visible : Visibility.Collapsed; _spacing.Value = CellSize;
            foreach (var (family, row) in _rows)
            {
                var style = styles.Get(family); string name = PerspectiveGuideEditing.Title(family, s.Mode);
                row.Row.Visibility = active.Contains(family) ? Visibility.Visible : Visibility.Collapsed;
                var color = style.GetColor(); row.Dot.Background = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B)); row.Name.Text = name; row.Summary.Text = $"{style.Opacity:0.#}%   {style.Density}";
                row.Select.Background = new SolidColorBrush(Color.FromArgb(SelectedFamily == family ? (byte)32 : (byte)0, 23, 105, 220));
                row.Row.Opacity = style.Opacity == 0 ? .5 : 1;
                AutomationProperties.SetName(row.Select, $"Edit {name}, opacity {style.Opacity:0.#} percent, density {style.Density}");
                AutomationProperties.SetName(row.Eye, (style.Opacity > 0 ? "Hide " : "Show ") + name);
            }
            _editor.Title = PerspectiveGuideEditing.Title(SelectedFamily, s.Mode); _editor.Settings = styles.Get(SelectedFamily);
            _editor.DensityVisibility = s.Mode == CanvasGridMode.Rectangular ? Visibility.Collapsed : Visibility.Visible;
            bool side = SelectedFamily is GridGuideFamily.Left or GridGuideFamily.Right && s.Mode != CanvasGridMode.FivePoint;
            bool center = SelectedFamily == GridGuideFamily.Center || SelectedFamily == GridGuideFamily.Top && s.Mode == CanvasGridMode.ThreePoint;
            _positionX.Visibility = side || center ? Visibility.Visible : Visibility.Collapsed;
            _positionX.Value = side ? SelectedFamily == GridGuideFamily.Left ? s.Left : s.Right : s.CenterX;
            _positionY.Visibility = SelectedFamily == GridGuideFamily.Top && s.Mode == CanvasGridMode.ThreePoint ? Visibility.Visible : Visibility.Collapsed; _positionY.Value = s.Vertical;
            _linked.Visibility = !side && !center && SelectedFamily != GridGuideFamily.Grid ? Visibility.Visible : Visibility.Collapsed;
            _line.IsChecked = ToolIndex == 1; _rectangle.IsChecked = ToolIndex == 2; _plane.SelectedIndex = (int)Plane;
        }
        finally { _refreshing = false; }
    }

    private sealed class NumericSetting : StackPanel
    {
        private readonly NumberBox _number;
        private readonly Slider _slider;
        private bool _setting;
        public event EventHandler<double>? Edited;
        public double Value { get => _number.Value; set { _setting = true; _number.Value = _slider.Value = value; _setting = false; } }
        public NumericSetting(string name, double min, double max)
        {
            Spacing = 0; _number = new() { Header = name, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            _slider = new() { Minimum = min, Maximum = max, StepFrequency = 1, Height = 28, MinHeight = 0 };
            AutomationProperties.SetName(_number, name); AutomationProperties.SetName(_slider, name + " slider"); Children.Add(_number); Children.Add(_slider);
            _number.ValueChanged += (_, args) => Change(args.NewValue); _slider.ValueChanged += (_, args) => Change(args.NewValue);
        }
        private void Change(double value) { if (_setting) return; if (!double.IsFinite(value)) { Value = _slider.Value; return; } Edited?.Invoke(this, value); }
    }
}
