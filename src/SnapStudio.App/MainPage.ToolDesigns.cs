using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SnapStudio.App.Controls;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using Windows.System;
using Windows.UI;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private Flyout? _toolDesignDrawer;
    private bool _toolDesignDialogIsOpen;
    private static readonly AnnotationKind[] DesignKinds = [AnnotationKind.Rectangle, AnnotationKind.Ellipse, AnnotationKind.Line,
        AnnotationKind.Arrow, AnnotationKind.Text, AnnotationKind.Highlight, AnnotationKind.Blur, AnnotationKind.Pen, AnnotationKind.Fill, AnnotationKind.Vector];
    private static string ToolShortcut(int? index) => index switch { null => "M", -1 => "V", 0 => "U", 1 => "O", 2 => "L", 3 => "A", 4 => "T", 5 => "H", 6 => "B", 7 => "P", 8 => "G", 9 => "K", _ => "" };
    private static string ToolName(AnnotationKind kind) => kind switch { AnnotationKind.Highlight => "Highlighter", AnnotationKind.Fill => "Fill bucket", AnnotationKind.Vector => "Custom shapes", _ => kind.ToString() };

    private void UpdateToolDesignBadges()
    {
        Button[] buttons = [RectangleToolButton, EllipseToolButton, LineToolButton, ArrowToolButton, TextToolButton,
            HighlightToolButton, BlurToolButton, PenToolButton, FillToolButton, CustomShapeToolButton];
        for (int index = 0; index < buttons.Length; index++)
            if (buttons[index].Content is ToolButtonContent badge)
            {
                badge.HasDesigns = ViewModel.GetToolDesigns(DesignKinds[index]).Any();
                badge.Pattern = ViewModel.GetToolPattern(DesignKinds[index]);
            }
    }

    private void HideToolDesignDrawer() { _toolDesignDrawer?.Hide(); }

    private void ToolDesignButton_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } button || !int.TryParse(tag, out int index)) return;
        args.Handled = true; ShowToolDesignDrawer(button, DesignKinds[index]);
    }

    private void ShowToolDesignDrawer(FrameworkElement anchor, AnnotationKind kind)
    {
        HideToolDesignDrawer();
        var content = new StackPanel { Width = 290, Spacing = 10 };
        content.Children.Add(new TextBlock { Text = $"{ToolName(kind)} designs · {ToolShortcut(ViewModels.ShellViewModel.ToolIndex(kind))}", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var defaults = new Button { Content = kind == AnnotationKind.Vector ? "Default shape" : "Default / solid", HorizontalAlignment = HorizontalAlignment.Stretch };
        defaults.Click += (_, _) => { HideToolDesignDrawer(); ViewModel.UseDefaultToolDesign(kind); UpdateBindingsFromViewModel(); CanvasScrollViewer.Focus(FocusState.Programmatic); };
        content.Children.Add(defaults);
        var list = new StackPanel { Spacing = 6 };
        foreach (ToolDesign design in ViewModel.GetToolDesigns(kind))
        {
            UIElement preview = design.Svg is { } svg ? new SvgThumbnail(svg) : new AnnotationToolIcon { Kind = kind.ToString(), Pattern = design.Style.Pattern, Width = 24, Height = 24 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(preview); row.Children.Add(new TextBlock { Text = design.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 });
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, $"Use {design.Name} design");
            button.Click += (_, _) => { HideToolDesignDrawer(); ViewModel.UseToolDesign(design); PropertiesSectionExpander.IsExpanded = true; UpdateBindingsFromViewModel(); CanvasScrollViewer.Focus(FocusState.Programmatic); };
            button.ContextRequested += (_, args) =>
            {
                args.Handled = true;
                var menu = new MenuFlyout(); var editSvg = new MenuFlyoutItem { Text = ViewModel.IsBuiltInDesign(design.Id) ? "Design SVG copy…" : "Edit SVG…" };
                editSvg.Click += async (_, _) => await EditSvgToolAsync(design, kind); menu.Items.Add(editSvg); menu.ShowAt(button);
            };
            list.Children.Add(button);
        }
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var create = new Button { Content = "Design SVG…", HorizontalAlignment = HorizontalAlignment.Stretch };
        create.Click += async (_, _) => await EditSvgToolAsync(null, kind);
        content.Children.Add(create);
        if (kind != AnnotationKind.Vector)
        {
            var style = new Button { Content = "Save a style preset…", HorizontalAlignment = HorizontalAlignment.Stretch };
            style.Click += async (_, _) => await EditToolDesignAsync(null, kind); content.Children.Add(style);
        }
        OpenToolDrawer(anchor, content);
    }

    private void DesignToolButton_Click(object sender, RoutedEventArgs args) => ShowDesignLibrary();

    private void OpenToolDrawer(FrameworkElement anchor, StackPanel content)
    {
        var drawer = new Flyout { Content = content, Placement = FlyoutPlacementMode.RightEdgeAlignedTop };
        drawer.Closed += (_, _) => { if (ReferenceEquals(_toolDesignDrawer, drawer)) _toolDesignDrawer = null; };
        _toolDesignDrawer = drawer; drawer.ShowAt(anchor);
    }

    private void ShowDesignLibrary()
    {
        HideToolDesignDrawer();
        var content = new StackPanel { Width = 330, Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "Design tools", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Draw custom SVG artwork, edit paths, or start from an existing tool in its drawer.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var create = new Button { Content = "New SVG tool…" };
        create.Click += async (_, _) => await EditSvgToolAsync(null, AnnotationKind.Vector);
        var importSvg = new Button { Content = "Import SVG…" };
        importSvg.Click += async (_, _) => await RunToolDesignUiActionAsync(async () =>
        {
            HideToolDesignDrawer();
            if (await ViewModel.ChooseSvgFileAsync(CancellationToken.None) is { } file)
                await EditSvgToolAsync(null, AnnotationKind.Vector, file.Svg, file.Name);
        });
        actions.Children.Add(create); actions.Children.Add(importSvg); content.Children.Add(actions);
        var packs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var import = new Button { Content = "Import library…" };
        import.Click += async (_, _) => { HideToolDesignDrawer(); await RunToolDesignUiActionAsync(() => ViewModel.ImportToolDesignsAsync(CancellationToken.None)); ShowDesignLibrary(); };
        var export = new Button { Content = "Export library…", IsEnabled = ViewModel.SavedToolDesigns.Count > 0 };
        export.Click += async (_, _) => { HideToolDesignDrawer(); await RunToolDesignUiActionAsync(() => ViewModel.ExportToolDesignsAsync(CancellationToken.None)); };
        packs.Children.Add(import); packs.Children.Add(export); content.Children.Add(packs);
        var search = new TextBox { PlaceholderText = "Search saved designs…" }; AutomationProperties.SetName(search, "Search tool designs"); content.Children.Add(search);
        var rows = new StackPanel { Spacing = 6 };
        void Populate()
        {
            rows.Children.Clear();
            foreach (ToolDesign design in ViewModel.SavedToolDesigns.Where(item => $"{item.Name} {ToolName(item.Kind)}".Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Name))
            {
                var row = new Grid { ColumnSpacing = 6 }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (design.Svg is { } svg) label.Children.Add(new SvgThumbnail(svg));
                label.Children.Add(new TextBlock { Text = $"{design.Name}\n{ToolName(design.Kind)} · {(design.Svg is null ? design.Style.Pattern.ToString() : "SVG")}", MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
                var use = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                use.Click += (_, _) => { HideToolDesignDrawer(); ViewModel.UseToolDesign(design); UpdateBindingsFromViewModel(); };
                var edit = new Button { Content = "…" }; Grid.SetColumn(edit, 1);
                var menu = new MenuFlyout();
                var modify = new MenuFlyoutItem { Text = "Edit SVG…" }; modify.Click += async (_, _) => await EditSvgToolAsync(design, design.Kind); menu.Items.Add(modify);
                if (design.Svg is null)
                {
                    var style = new MenuFlyoutItem { Text = "Edit style preset…" }; style.Click += async (_, _) => await EditToolDesignAsync(design, design.Kind); menu.Items.Add(style);
                }
                var remove = new MenuFlyoutItem { Text = "Remove design…" };
                remove.Click += async (_, _) =>
                {
                    HideToolDesignDrawer(); _toolDesignDialogIsOpen = true;
                    try
                    {
                        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Remove tool design?", Content = $"Remove {design.Name}? Existing objects keep their saved styles.", PrimaryButtonText = "Remove", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await RunToolDesignUiActionAsync(() => ViewModel.RemoveToolDesignAsync(design.Id, CancellationToken.None));
                    }
                    finally { _toolDesignDialogIsOpen = false; }
                    ShowDesignLibrary();
                };
                menu.Items.Add(remove); edit.Flyout = menu; row.Children.Add(use); row.Children.Add(edit); rows.Children.Add(row);
            }
            if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No saved designs match. Create a design or import a design file.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        search.TextChanged += (_, _) => Populate(); Populate();
        content.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        OpenToolDrawer(DesignToolButton, content);
    }

    private async Task RunToolDesignUiActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { ViewModel.ShowCaptureHistoryError($"Tool design: {exception.Message}"); }
        finally { UpdateBindingsFromViewModel(); }
    }

    private async Task EditToolDesignAsync(ToolDesign? existing, AnnotationKind kind)
    {
        if (_toolDesignDialogIsOpen || _captureTagsDialogIsOpen || _captureRenameDialogIsOpen || _captureDeleteDialogIsOpen || _newImageDialogIsOpen || _pasteImageDialogIsOpen || _layerRenameDialogIsOpen || _layerColorDialogIsOpen || _historyFolderDialogIsOpen) return;
        HideToolDesignDrawer(); _toolDesignDialogIsOpen = true;
        try
        {
            AnnotationStyle initial = existing?.Style ?? ViewModel.DrawingStyle;
            if (existing is null && kind == AnnotationKind.Text && ViewModel.ActiveAnnotationKind != AnnotationKind.Text)
                initial = initial with { StrokeThickness = 24, Text = initial.Stroke };
            var name = new TextBox { Header = "Design name", Text = existing?.Name ?? $"{ToolName(kind)} design", MaxLength = 80 };
            var tool = new ComboBox { Header = "Base tool", ItemsSource = DesignKinds.Where(item => item != AnnotationKind.Vector).Select(ToolName).ToArray(), SelectedIndex = ViewModels.ShellViewModel.ToolIndex(kind), HorizontalAlignment = HorizontalAlignment.Stretch };
            var pattern = new ComboBox { Header = "Line style", ItemsSource = new[] { "Solid", "Dashed", "Dotted", "Dash dot" }, SelectedIndex = (int)initial.Pattern, HorizontalAlignment = HorizontalAlignment.Stretch };
            var width = new TextBox { Header = "Stroke width", Text = initial.StrokeThickness.ToString(CultureInfo.CurrentCulture), InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
            var corners = new TextBox { Header = "Corner radius", Text = initial.CornerRadius.ToString(CultureInfo.CurrentCulture) };
            var opacity = new Slider { Header = "Opacity", Minimum = 0.1, Maximum = 1, StepFrequency = 0.05, Value = initial.Opacity };
            var font = new ComboBox { Header = "Font", ItemsSource = ViewModel.AnnotationFontFamilies, SelectedItem = SnapStudio.Rendering.SystemFontCatalog.ResolveFamily(initial.FontFamily), HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSearchEnabled = true };
            var color = new TextBox { Header = "Color (#RRGGBB or #AARRGGBB)", Text = FormatDesignColor(kind == AnnotationKind.Text ? initial.Text : kind is AnnotationKind.Highlight or AnnotationKind.Fill ? initial.Fill : initial.Stroke) };
            var fill = new TextBox { Header = "Fill color (#AARRGGBB)", Text = FormatDesignColor(initial.Fill) };
            var colorRow = DesignColorRow(color); var fillRow = DesignColorRow(fill);
            var preview = new ToolDesignPreview();
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var form = new StackPanel { Spacing = 10, Width = 330 };
            foreach (UIElement element in new UIElement[] { name, tool, preview, pattern, width, colorRow, fillRow, opacity, corners, font, error }) form.Children.Add(element);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = existing is null ? "Create tool design" : "Edit tool design", Content = new ScrollViewer { Content = form, MaxHeight = 470, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, PrimaryButtonText = "Save design", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            Guid id = existing?.Id ?? Guid.NewGuid();
            bool TryBuild(out ToolDesign? result)
            {
                result = null;
                try
                {
                    AnnotationKind selected = DesignKinds[Math.Max(0, tool.SelectedIndex)];
                    bool stroked = ToolDesigns.SupportsPattern(selected), text = selected == AnnotationKind.Text, shape = selected is AnnotationKind.Rectangle or AnnotationKind.Ellipse;
                    bool hasColor = selected != AnnotationKind.Blur;
                    pattern.IsEnabled = stroked; if (!stroked) pattern.SelectedIndex = 0;
                    width.Visibility = stroked || text ? Visibility.Visible : Visibility.Collapsed; width.Header = text ? "Text size" : "Stroke width";
                    corners.Visibility = selected == AnnotationKind.Rectangle ? Visibility.Visible : Visibility.Collapsed;
                    font.Visibility = text ? Visibility.Visible : Visibility.Collapsed; colorRow.Visibility = hasColor ? Visibility.Visible : Visibility.Collapsed;
                    fillRow.Visibility = shape ? Visibility.Visible : Visibility.Collapsed; opacity.Header = selected == AnnotationKind.Blur ? "Blur strength" : "Opacity";
                    double size = stroked || text ? double.Parse(width.Text, CultureInfo.CurrentCulture) : 1;
                    double radius = selected == AnnotationKind.Rectangle ? double.Parse(corners.Text, CultureInfo.CurrentCulture) : 0;
                    ColorRgba foreground = hasColor ? ParseDesignColor(color.Text) : ColorRgba.Transparent;
                    var style = new AnnotationStyle(stroked ? foreground : ColorRgba.Transparent, shape ? ParseDesignColor(fill.Text) : selected is AnnotationKind.Fill or AnnotationKind.Highlight ? foreground : ColorRgba.Transparent,
                        text ? foreground : ColorRgba.Black, size, opacity.Value, radius)
                    { Pattern = (StrokePattern)pattern.SelectedIndex, FontFamily = font.SelectedItem as string ?? AnnotationStyle.DefaultFontFamily };
                    result = ToolDesigns.Validate(new(id, name.Text, selected, style));
                    string proposedName = result.Name;
                    if (ViewModel.SavedToolDesigns.Any(item => item.Id != id && item.Kind == selected && item.Name.Equals(proposedName, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("A design with this name already exists for this tool.");
                    error.Text = string.Empty; return true;
                }
                catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException or InvalidDataException) { error.Text = exception.Message; return false; }
            }
            bool refreshing = false;
            void Refresh()
            {
                if (refreshing) return; refreshing = true;
                try { dialog.IsPrimaryButtonEnabled = TryBuild(out ToolDesign? draft); if (draft is not null) preview.Update(draft); }
                finally { refreshing = false; }
            }
            foreach (TextBox input in new[] { name, width, corners, color, fill }) input.TextChanged += (_, _) => Refresh();
            AnnotationKind previousKind = kind;
            tool.SelectionChanged += (_, _) =>
            {
                AnnotationKind nextKind = DesignKinds[Math.Max(0, tool.SelectedIndex)];
                if (nextKind == AnnotationKind.Text && previousKind != AnnotationKind.Text) width.Text = "24";
                else if (previousKind == AnnotationKind.Text && ToolDesigns.SupportsPattern(nextKind)) width.Text = "3";
                previousKind = nextKind;
                Refresh();
            };
            foreach (ComboBox combo in new[] { pattern, font }) combo.SelectionChanged += (_, _) => Refresh();
            opacity.ValueChanged += (_, _) => Refresh(); Refresh();
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                if (!TryBuild(out ToolDesign? draft) || draft is null) { args.Cancel = true; return; }
                var deferral = args.GetDeferral();
                try { await ViewModel.SaveToolDesignAsync(draft, CancellationToken.None); }
                catch (Exception exception) { args.Cancel = true; error.Text = exception.Message; }
                finally { deferral.Complete(); }
            };
            await dialog.ShowAsync(); UpdateBindingsFromViewModel();
        }
        catch (Exception exception) { ViewModel.ShowCaptureHistoryError($"Tool design: {exception.Message}"); }
        finally { _toolDesignDialogIsOpen = false; }
    }

    private Grid DesignColorRow(TextBox input)
    {
        var row = new Grid { ColumnSpacing = 6 }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var choose = new Button { Content = "Pick…", VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumn(choose, 1);
        choose.Click += (_, _) =>
        {
            ColorRgba value; try { value = ParseDesignColor(input.Text); } catch { value = ColorRgba.Black; }
            var picker = new ColorPicker { Color = Color.FromArgb(value.A, value.R, value.G, value.B), IsAlphaEnabled = true };
            picker.ColorChanged += (_, args) => input.Text = FormatDesignColor(new(args.NewColor.R, args.NewColor.G, args.NewColor.B, args.NewColor.A));
            new Flyout { Content = picker }.ShowAt(choose);
        };
        row.Children.Add(input); row.Children.Add(choose); return row;
    }
    private static string FormatDesignColor(ColorRgba color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    private static ColorRgba ParseDesignColor(string text)
    {
        string hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8)) throw new FormatException("Enter a color as #RRGGBB or #AARRGGBB.");
        uint value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new((byte)(value >> 16), (byte)(value >> 8), (byte)value, hex.Length == 8 ? (byte)(value >> 24) : (byte)255);
    }

    private async void ToolKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_canvasDragMode != CanvasDragMode.None || !TryHandleApplicationShortcut(args, sender.Key != VirtualKey.C || ViewModel.CanCropToSelection)) return;
        if (sender.Key == VirtualKey.D) { ShowDesignLibrary(); return; }
        if (sender.Key == VirtualKey.S) { ShowStampPicker(); return; }
        if (sender.Key == VirtualKey.W) { ViewModel.PixelSelectionToolIndex = (int)PixelSelectionTool.MagicWand; SelectCanvasTool(null); return; }
        if (sender.Key == VirtualKey.C) { await ViewModel.CropToSelectedAnnotationAsync(CancellationToken.None); UpdateBindingsFromViewModel(); FitCurrentCanvas(); return; }
        int? index = sender.Key switch
        {
            VirtualKey.V => -1,
            VirtualKey.U => 0,
            VirtualKey.O => 1,
            VirtualKey.L => 2,
            VirtualKey.A => 3,
            VirtualKey.T => 4,
            VirtualKey.H => 5,
            VirtualKey.B => 6,
            VirtualKey.P => 7,
            VirtualKey.G => 8,
            VirtualKey.K => 9,
            _ => null
        };
        SelectCanvasTool(index);
    }

    private async void AnnotationPattern_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ShouldIgnoreControlEvent() || sender is not ComboBox { SelectedIndex: >= 0 } combo || combo.SelectedIndex == ViewModel.AnnotationPatternIndex) return;
        await ViewModel.UpdateAnnotationPatternAsync(combo.SelectedIndex, CancellationToken.None); UpdateBindingsFromViewModel();
    }
}
