using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.App.Controls;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private Flyout? _strokePatternFlyout;

    private void CustomStrokePicker_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement anchor) ShowStrokePatternPicker(anchor);
    }

    private void ShowStrokePatternPicker(FrameworkElement anchor)
    {
        HideToolDesignDrawer(); _strokePatternFlyout?.Hide();
        var content = new StackPanel { Width = 290, Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "Custom line patterns", FontSize = 16 });
        var rows = new StackPanel { Spacing = 5 };
        foreach (ToolDesign pattern in ViewModel.SavedStrokePatterns)
        {
            var row = new Grid { ColumnSpacing = 5 }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(new SvgThumbnail(pattern.Svg!, 28)); label.Children.Add(new TextBlock { Text = pattern.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 });
            var use = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(use, $"Use {pattern.Name} line pattern");
            use.Click += async (_, _) => { _strokePatternFlyout?.Hide(); await RunToolDesignUiActionAsync(() => ViewModel.UseStrokePatternAsync(pattern, CancellationToken.None)); };
            var edit = new Button { Content = "Edit", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(edit, 1);
            edit.Click += async (_, _) => { _strokePatternFlyout?.Hide(); await EditStrokePatternAsync(pattern); };
            row.Children.Add(use); row.Children.Add(edit); rows.Children.Add(row);
        }
        if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "Design a motif in the SVG editor, then reuse it with any stroked tool.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 280 });
        var create = new Button { Content = "New SVG pattern…", HorizontalAlignment = HorizontalAlignment.Stretch };
        create.Click += async (_, _) => { _strokePatternFlyout?.Hide(); await EditStrokePatternAsync(null); }; content.Children.Add(create);
        content.Children.Add(new TextBlock { Text = "Patterns keep their SVG colors. Width controls motif height; scale and spacing adjust the repeat.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        _strokePatternFlyout = new Flyout { Content = content, Placement = FlyoutPlacementMode.LeftEdgeAlignedTop };
        _strokePatternFlyout.Closed += (_, _) => { if (anchor is ComboBox combo) combo.SelectedIndex = ViewModel.AnnotationPatternIndex; };
        _strokePatternFlyout.ShowAt(anchor);
    }

    private async void NewStrokePattern_Click(object sender, RoutedEventArgs args) => await EditStrokePatternAsync(null);

    private async Task EditStrokePatternAsync(ToolDesign? existing)
    {
        if (!CanOpenSvgDesigner) return;
        var name = new TextBox { Header = "Pattern name", Text = existing?.Name ?? "Line pattern", MaxLength = 80, MinWidth = 220 };
        var heading = new StackPanel { Spacing = 5 }; heading.Children.Add(name);
        heading.Children.Add(new TextBlock { Text = "Draw one repeat. It follows the line and rotates with curves. Leave space in the canvas for gaps between motifs.", TextWrapping = TextWrapping.Wrap });
        await ShowSvgDesignerAsync("SVG line pattern", heading, existing?.Svg ?? CustomStrokeGeometry.StarterSvg, async source =>
        {
            var pattern = ToolDesigns.Validate(new ToolDesign(existing?.Id ?? Guid.NewGuid(), name.Text, AnnotationKind.Line, AnnotationStyle.Default) { IsStrokePattern = true, Svg = source });
            await ViewModel.SaveToolDesignAsync(pattern, CancellationToken.None);
            if (ViewModel.AnnotationPatternControlsVisibility == Visibility.Visible) await ViewModel.UseStrokePatternAsync(pattern, CancellationToken.None);
        }, "Save pattern");
    }

    private async void CustomStrokeSettings_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (ShouldIgnoreControlEvent() || !double.IsFinite(args.NewValue) || ViewModel.CurrentCustomStrokePattern is not { } pattern) return;
        double value = Math.Clamp(args.NewValue / 100, .1, 10);
        var next = sender.Tag is "Scale" ? pattern with { Scale = value } : pattern with { Spacing = value };
        if (next == pattern) return;
        await RunToolDesignUiActionAsync(() => ViewModel.SetCustomStrokeAsync(next, CancellationToken.None));
    }

    private void AddCustomStrokePreview(FrameworkElement element, AnnotationOverlayItem item)
    {
        if (item.CustomStroke is not { } preview || element is not Panel root) return;
        var bitmap = new BitmapImage(); using var stream = new MemoryStream(preview.Image.PngBytes, writable: false); bitmap.SetSource(stream.AsRandomAccessStream());
        var image = new Image { Source = bitmap, Width = preview.Bounds.Width, Height = preview.Bounds.Height, Opacity = item.Opacity, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Canvas.SetLeft(image, preview.Bounds.X); Canvas.SetTop(image, preview.Bounds.Y);
        var drawing = new Canvas { IsHitTestVisible = false, Clip = new RectangleGeometry { Rect = new(-item.Left, -item.Top, ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight) } }; drawing.Children.Add(image);
        int position = root.Children.Count;
        for (int i = 0; i < root.Children.Count; i++) if (root.Children[i] is FrameworkElement { Tag: string name } && Enum.TryParse<AnnotationBoundsHandle>(name, out _)) { position = i; break; }
        root.Children.Insert(position, drawing);
    }
}
