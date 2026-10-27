using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace SnapStudio.App.Controls;

public sealed class PerspectiveGuideHandles : Canvas
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(PerspectiveGuideHandles),
        new PropertyMetadata(value, (sender, _) => ((PerspectiveGuideHandles)sender).Refresh()));
    public static readonly DependencyProperty PerspectiveProperty = Property(nameof(Perspective), typeof(PerspectiveGridSettings), null);
    public static readonly DependencyProperty FamiliesProperty = Property(nameof(Families), typeof(CanvasGridFamilies), null);
    public static readonly DependencyProperty ZoomProperty = Property(nameof(Zoom), typeof(double), 1d);
    public static readonly DependencyProperty SelectedFamilyProperty = Property(nameof(SelectedFamily), typeof(GridGuideFamily), GridGuideFamily.Left);
    public PerspectiveGridSettings? Perspective { get => (PerspectiveGridSettings?)GetValue(PerspectiveProperty); set => SetValue(PerspectiveProperty, value); }
    public CanvasGridFamilies? Families { get => (CanvasGridFamilies?)GetValue(FamiliesProperty); set => SetValue(FamiliesProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public GridGuideFamily SelectedFamily { get => (GridGuideFamily)GetValue(SelectedFamilyProperty); set => SetValue(SelectedFamilyProperty, value); }
    public bool IsEditing => _original is not null;
    public bool OwnsKeyboardFocus
    {
        get { if (XamlRoot is null) return false; for (DependencyObject? node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node)) if (ReferenceEquals(node, this)) return true; return false; }
    }

    private readonly Rectangle _horizonHit = new() { Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Height = 12, Tag = PerspectiveGuideHandleKind.Horizon };
    private readonly Line _horizonLine = new() { Stroke = new SolidColorBrush(Color.FromArgb(180, 100, 116, 139)), StrokeThickness = 1, StrokeDashArray = new() { 5, 5 }, IsHitTestVisible = false };
    private readonly Dictionary<PerspectiveGuideHandleKind, (Button Button, Border Dot, TextBlock Label)> _buttons = [];
    private PerspectiveGridSettings? _original;
    private PerspectiveGuideHandleKind _dragKind;
    private Point _start;
    private SizeD _dragImage;
    private double _dragZoom;
    private bool _ownUpdate;

    public PerspectiveGuideHandles()
    {
        Children.Add(_horizonHit); Children.Add(_horizonLine);
        foreach (PerspectiveGuideHandleKind kind in Enum.GetValues<PerspectiveGuideHandleKind>())
        {
            var dot = new Border { Width = 9, Height = 9, CornerRadius = new(5), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; content.Children.Add(dot); content.Children.Add(label);
            var button = new Button { Content = content, Padding = new(5, 3, 5, 3), MinHeight = 24, MinWidth = 0, Tag = kind,
                Background = new SolidColorBrush(Color.FromArgb(235, 248, 250, 253)), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)) };
            if (kind != PerspectiveGuideHandleKind.Horizon) { content.Children.Remove(dot); content.Children.Remove(label); button.Content = dot; button.Width = button.Height = 24; button.Padding = new(4); Children.Add(label); }
            button.KeyDown += HandleKey; _buttons.Add(kind, (button, dot, label)); Children.Add(button);
        }
        AddHandler(PointerPressedEvent, new PointerEventHandler(Pressed), true);
        PointerMoved += Moved; PointerReleased += Released;
        PointerCanceled += (_, _) => CancelEdit();
        PointerCaptureLost += (_, args) => { if (ReferenceEquals(args.OriginalSource, this)) CancelEdit(); };
        SizeChanged += (_, _) => { CancelEdit(); Refresh(); };
        Unloaded += (_, _) => CancelEdit();
        AutomationProperties.SetName(this, "Perspective guide points");
    }

    public void CancelEdit()
    {
        var original = _original; _original = null;
        if (original is not null) SetPerspective(original);
        ReleasePointerCaptures();
    }

    private void SetPerspective(PerspectiveGridSettings settings)
    {
        _ownUpdate = true; try { Perspective = settings; } finally { _ownUpdate = false; }
    }

    private void Pressed(object sender, PointerRoutedEventArgs args)
    {
        var pointer = args.GetCurrentPoint(this);
        if (!pointer.Properties.IsLeftButtonPressed || _original is not null || Zoom <= 0 || Perspective is not { Mode: not CanvasGridMode.Rectangular }) return;
        FrameworkElement? target = args.OriginalSource as FrameworkElement;
        while (target is not null && target.Tag is not PerspectiveGuideHandleKind) target = VisualTreeHelper.GetParent(target) as FrameworkElement;
        if (target?.Tag is not PerspectiveGuideHandleKind kind) return;
        args.Handled = true; _original = Perspective; _dragKind = kind; _start = pointer.Position; _dragZoom = Zoom;
        _dragImage = new(ActualWidth / Zoom, ActualHeight / Zoom);
        if (kind != PerspectiveGuideHandleKind.Horizon) SelectedFamily = Enum.Parse<GridGuideFamily>(kind.ToString());
        _buttons[kind].Button.Focus(FocusState.Pointer);
        // Button press behavior can capture first; transfer ownership to the persistent overlay.
        _buttons[kind].Button.ReleasePointerCapture(args.Pointer);
        if (!CapturePointer(args.Pointer)) _original = null;
    }

    private void Moved(object sender, PointerRoutedEventArgs args)
    {
        if (_original is null) return;
        Point point = args.GetCurrentPoint(this).Position;
        SetPerspective(PerspectiveGuideEditing.Move(_original, _dragImage, _dragKind, new((point.X - _start.X) / _dragZoom, (point.Y - _start.Y) / _dragZoom)));
        args.Handled = true;
    }

    private void Released(object sender, PointerRoutedEventArgs args)
    {
        if (_original is null) return;
        Moved(sender, args); _original = null; ReleasePointerCapture(args.Pointer); args.Handled = true;
    }

    private void HandleKey(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape) { CancelEdit(); args.Handled = true; return; }
        if (sender is not Button { Tag: PerspectiveGuideHandleKind kind } || Perspective is null || Zoom <= 0) return;
        PointD delta = args.Key switch { VirtualKey.Left => new(-1, 0), VirtualKey.Right => new(1, 0), VirtualKey.Up => new(0, -1), VirtualKey.Down => new(0, 1), _ => new(0, 0) };
        if (delta.X == 0 && delta.Y == 0) return;
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down)) delta = new(delta.X * 10, delta.Y * 10);
        SetPerspective(PerspectiveGuideEditing.Move(Perspective, new(ActualWidth / Zoom, ActualHeight / Zoom), kind, delta)); args.Handled = true;
    }

    private void Refresh()
    {
        if (_original is not null && !_ownUpdate && (Perspective?.Mode != _original.Mode || Math.Abs(Zoom - _dragZoom) > 1e-8)) { _original = null; ReleasePointerCaptures(); }
        if (Perspective is not { Mode: not CanvasGridMode.Rectangular } settings || ActualWidth < 1 || ActualHeight < 1 || Zoom <= 0) return;
        foreach (var item in _buttons.Values) { item.Button.Visibility = Visibility.Collapsed; item.Label.Visibility = Visibility.Collapsed; }
        double y = Math.Clamp(ActualHeight * settings.Horizon / 100, 8, Math.Max(8, ActualHeight - 8));
        _horizonHit.Width = ActualWidth; SetTop(_horizonHit, y - 6);
        _horizonLine.X1 = 0; _horizonLine.X2 = ActualWidth; _horizonLine.Y1 = _horizonLine.Y2 = y;
        var horizon = _buttons[PerspectiveGuideHandleKind.Horizon]; horizon.Button.Visibility = horizon.Label.Visibility = Visibility.Visible; horizon.Label.Text = $"Horizon {settings.Horizon:0.#}%";
        horizon.Label.Foreground = horizon.Dot.Background = new SolidColorBrush(Color.FromArgb(255, 71, 85, 105)); SetLeft(horizon.Button, 4); SetTop(horizon.Button, Math.Max(0, y - 29));
        AutomationProperties.SetName(horizon.Button, $"Move horizon, {settings.Horizon:0.#} percent");
        foreach (var handle in PerspectiveGuideEditing.Handles(new(ActualWidth / Zoom, ActualHeight / Zoom), settings))
        {
            var style = (Families ?? new()).Get(handle.Family); if (style.Opacity == 0) continue;
            var item = _buttons[handle.Kind]; string name = PerspectiveGuideEditing.Title(handle.Family, settings.Mode);
            bool outside = handle.Position.X < 0 || handle.Position.X > ActualWidth / Zoom || handle.Position.Y < 0 || handle.Position.Y > ActualHeight / Zoom;
            var color = style.GetColor(); var brush = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
            string direction = handle.Position.X < 0 ? " ←" : handle.Position.X > ActualWidth / Zoom ? " →" : handle.Position.Y < 0 ? " ↑" : handle.Position.Y > ActualHeight / Zoom ? " ↓" : "";
            item.Dot.Background = item.Label.Foreground = brush; item.Label.Text = name + direction; item.Button.BorderBrush = brush;
            item.Button.BorderThickness = new(SelectedFamily == handle.Family ? 2 : 1); item.Button.Visibility = item.Label.Visibility = Visibility.Visible;
            double x = Math.Clamp(handle.Position.X * Zoom, 12, Math.Max(12, ActualWidth - 12)), py = Math.Clamp(handle.Position.Y * Zoom, 12, Math.Max(12, ActualHeight - 12));
            SetLeft(item.Button, x - 12); SetTop(item.Button, py - 12);
            SetLeft(item.Label, Math.Clamp(x + 14, 0, Math.Max(0, ActualWidth - 82))); SetTop(item.Label, Math.Clamp(py + 13, 0, Math.Max(0, ActualHeight - 18)));
            AutomationProperties.SetName(item.Button, $"Move {name}{(outside ? ", off canvas" : "")}");
            ToolTipService.SetToolTip(item.Button, $"{name}: {handle.Position.X * Zoom / ActualWidth * 100:0.#}%, {handle.Position.Y * Zoom / ActualHeight * 100:0.#}%. Drag or use arrow keys.");
        }
    }
}
