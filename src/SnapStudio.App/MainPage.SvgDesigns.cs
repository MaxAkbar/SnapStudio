using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.App.Controls;
using SnapStudio.Core.Documents;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool CanOpenSvgDesigner => !_stampDialogIsOpen && !_toolDesignDialogIsOpen && !_captureTagsDialogIsOpen && !_captureRenameDialogIsOpen
        && !_captureDeleteDialogIsOpen && !_newImageDialogIsOpen && !_pasteImageDialogIsOpen && !_layerRenameDialogIsOpen
        && !_layerColorDialogIsOpen && !_historyFolderDialogIsOpen;

    private async Task EditSvgToolAsync(ToolDesign? existing, AnnotationKind family, string? initialSvg = null, string? initialName = null)
    {
        if (!CanOpenSvgDesigner) return;
        var name = new TextBox { Header = "Tool name", Text = initialName ?? existing?.Name ?? $"{ToolName(family)} design", MaxLength = 80, MinWidth = 220 };
        var tool = new ComboBox { Header = "Tool drawer", ItemsSource = DesignKinds.Select(ToolName).ToArray(), SelectedIndex = ViewModels.ShellViewModel.ToolIndex(family), MinWidth = 180 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(name); heading.Children.Add(tool);
        AnnotationStyle style = existing?.Style ?? (ViewModel.ActiveAnnotationKind == AnnotationKind.Vector ? AnnotationStyle.Default : ViewModel.DrawingStyle);
        string svg = initialSvg ?? existing?.Svg ?? SvgToolTemplates.Create(family, style);
        Guid id = existing is not null && !ViewModel.IsBuiltInDesign(existing.Id) ? existing.Id : Guid.NewGuid();
        await ShowSvgDesignerAsync("SVG tool designer", heading, svg, async source =>
        {
            AnnotationKind selected = DesignKinds[Math.Max(0, tool.SelectedIndex)];
            ToolDesign design = ToolDesigns.Validate(new(id, name.Text, selected, AnnotationStyle.Default) { Svg = source });
            await ViewModel.SaveToolDesignAsync(design, CancellationToken.None);
            ViewModel.UseToolDesign(design);
        }, "Save tool", importedName => { if (existing is null) name.Text = importedName; });
    }

    private async void EditSelectedVector_Click(object sender, RoutedEventArgs args) => await EditSelectedVectorAsync();

    private async Task EditSelectedVectorAsync()
    {
        if (!CanOpenSvgDesigner || ViewModel.SelectedVector is not { } vector || ViewModel.CurrentDocumentId is not { } documentId
            || ViewModel.SelectedAnnotationId is not { } annotationId) return;
        var heading = new TextBlock { Text = "Edit this object's artwork. The reusable tool keeps its own design.", TextWrapping = TextWrapping.Wrap };
        await ShowSvgDesignerAsync($"Edit SVG · {vector.Name}", heading, vector.Svg,
            source => ViewModel.UpdateVectorAsync(documentId, annotationId, source, CancellationToken.None), "Apply to object");
    }

    private async Task ShowSvgDesignerAsync(string title, UIElement heading, string svg, Func<string, Task> save, string saveLabel, Action<string>? importedName = null)
    {
        HideToolDesignDrawer(); _toolDesignDialogIsOpen = true;
        using var editor = new SvgDesignerControl(svg, ViewModel.AnnotationFontFamilies.ToArray(), ActualTheme,
            new(ViewModel.ShowCanvasGrid, ViewModel.CanvasGridCellSize, ViewModel.SnapToCanvasGrid));
        editor.GridSettingsChanged += (_, settings) =>
        {
            ViewModel.CanvasGridCellSize = settings.Spacing;
            ViewModel.SnapToCanvasGrid = settings.Snap;
            ViewModel.ShowCanvasGrid = settings.Visible;
        };
        try
        {
            double width = Math.Max(640, Math.Min(1180, XamlRoot.Size.Width - 96));
            double height = Math.Max(260, Math.Min(620, XamlRoot.Size.Height - 360));
            var content = new Grid { Width = width, RowSpacing = 10 };
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            content.RowDefinitions.Add(new() { Height = new(height) });
            content.Children.Add(heading); Grid.SetRow(editor, 1); content.Children.Add(editor);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = title,
                Content = content,
                PrimaryButtonText = saveLabel,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.None,
                IsPrimaryButtonEnabled = false
            };
            dialog.Resources["ContentDialogMaxWidth"] = width + 56;
            bool saving = false;
            async Task<bool> SaveAsync()
            {
                if (saving) return false; saving = true; dialog.IsPrimaryButtonEnabled = false;
                try { await save(await editor.GetSvgAsync()); return true; }
                catch (Exception error) { editor.ShowError(error.Message); return false; }
                finally { saving = false; dialog.IsPrimaryButtonEnabled = true; }
            }
            editor.Ready += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            editor.ImportRequested += async (_, _) =>
            {
                try
                {
                    if (await ViewModel.ChooseSvgFileAsync(CancellationToken.None) is { } file)
                    { await editor.LoadSvgAsync(file.Svg); importedName?.Invoke(file.Name); }
                }
                catch (Exception error) { editor.ShowError(error.Message); }
            };
            editor.ExportRequested += async (_, _) =>
            {
                try { await ViewModel.ExportSvgAsync(await editor.GetSvgAsync(), CancellationToken.None); }
                catch (Exception error) { editor.ShowError(error.Message); }
            };
            editor.SaveRequested += async (_, _) => { if (await SaveAsync()) dialog.Hide(); };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try { args.Cancel = !await SaveAsync(); } finally { deferral.Complete(); }
            };
            await dialog.ShowAsync();
        }
        catch (Exception error) { ViewModel.ShowCaptureHistoryError("SVG designer: " + error.Message); }
        finally { _toolDesignDialogIsOpen = false; UpdateBindingsFromViewModel(); RefreshAnnotationCanvas(); }
    }
}
