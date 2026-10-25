using System.Collections.Specialized;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SnapStudio.App.Composition;
using SnapStudio.App.ViewModels;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace SnapStudio.App;

public sealed partial class MainPage : Page
{
    private const double MinimumDragDisplaySize = 4;
    private const double HistoryCollapsedWidth = 52;
    private const double HistoryExpandedDefaultWidth = 268;
    private const double HistoryMinimumWidth = 226;
    private const double HistoryMaximumWidth = 560;
    private const double HistorySplitterWidth = 4;
    private const double RightControlsCollapsedWidth = 52;
    private const double RightControlsExpandedDefaultWidth = 272;
    private const double RightControlsMinimumWidth = 238;
    private const double RightControlsMaximumWidth = 560;
    private const double RightControlsSplitterWidth = 4;
    private const double EditorMinimumWidth = 412;

    private CanvasDragMode _canvasDragMode;
    private Guid _annotationDragId;
    private bool _annotationDragHasMoved;
    private AnnotationBoundsHandle _annotationDragHandle = AnnotationBoundsHandle.Move;
    private RectD _annotationDragOriginalSourceBounds;
    private PointD _annotationDragStartSourcePoint;
    private Point _annotationDragStart;
    private readonly List<PointD> _penStrokePoints = [];
    private bool _isUpdatingBindings;
    private bool _isReadyForControlEvents;
    private bool _newImageDialogIsOpen;
    private bool _pasteImageDialogIsOpen;
    private bool _layerRenameDialogIsOpen;
    private bool _layerColorDialogIsOpen;
    private bool _historyPaneIsExpanded = true;
    private bool _historySplitterIsDragging;
    private Point _historySplitterStartPoint;
    private double _historySplitterStartWidth;
    private double _historyExpandedWidth = HistoryExpandedDefaultWidth;
    private bool _rightControlsPaneIsExpanded = true;
    private bool _rightControlsSplitterIsDragging;
    private Point _rightControlsSplitterStartPoint;
    private double _rightControlsSplitterStartWidth;
    private double _rightControlsExpandedWidth = RightControlsExpandedDefaultWidth;
    private ShellViewModel? _annotationOverlayViewModel;
    private readonly ConditionalWeakTable<AnnotationImage, Microsoft.UI.Xaml.Media.Imaging.BitmapImage> _annotationImageSources = new();

    public MainPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyAppTheme();
        Unloaded += (_, _) => StopMarqueeAnimation();
        ActualThemeChanged += (_, _) =>
        {
            UpdateAnnotationToolbarSelection();
            NotifyWorkspacePresentationChanged();
        };
    }

    public ShellViewModel ViewModel { get; private set; } = ShellViewModel.Empty;

    private enum CanvasDragMode
    {
        None,
        SelectObjects,
        SelectPolygon,
        MoveMarquee,
        DrawAnnotation,
        DrawPen,
        MoveAnnotation,
        ResizeAnnotation
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not AppServices services)
        {
            return;
        }

        ViewModel = new ShellViewModel(
            services.SettingsStore,
            services.SettingsImportExportService,
            services.SettingsFilePicker,
            services.DocumentCatalog,
            services.DocumentRepository,
            services.DocumentThumbnailCache,
            services.DocumentWorkspaceBootstrapper,
            services.EditCommandStack,
            services.DocumentRenderer,
            services.DocumentRasterEditor,
            services.CaptureTargetSelector,
            services.CaptureWorkflow,
            services.ImageImportService,
            services.ImageCreationService,
            services.ClipboardService,
            services.ExportDestinationPicker,
            services.ExportProviders,
            services.HotkeyService,
            services.WorkspaceShellService,
            services.FileTrashService,
            services.PinnedImageService,
            services.StorageLocationPicker,
            services.DiagnosticLog,
            services.CrashRecoveryJournal,
            services.RecoverySessionId,
            services.FeatureFlags,
            services.OcrTextExtractionService,
            services.ScrollTargetDetector,
            services.ScrollingCaptureService,
            services.ScreenRecordingService,
            services.EditorMessageServer,
            DispatcherQueue,
            services.HistoryFolderStore,
            services.ToolDesignStore,
            services.ToolDesignFilePicker);
        SubscribeAnnotationOverlays(ViewModel);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.ThemePreference)) ApplyAppTheme();
            UpdateBindingsFromViewModel();
            if (args.PropertyName is nameof(ShellViewModel.AnnotationOverlays) or nameof(ShellViewModel.CanvasImageSource)
                or nameof(ShellViewModel.CanvasDisplayWidth) or nameof(ShellViewModel.CanvasDisplayHeight))
                RefreshAnnotationCanvas();
        };

        await ViewModel.LoadAsync(CancellationToken.None);
        ViewModel.StartEditorMessageLoop();

        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
        FitCurrentCanvas();
        _isReadyForControlEvents = true;
    }

    private async void CaptureRegionKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args))
        {
            return;
        }

        await CaptureAndFitAsync(CaptureMode.Region, CancellationToken.None);
    }

    private async void OpenKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanOpenImageFile))
        {
            return;
        }

        await OpenAndFitAsync(CancellationToken.None);
    }

    private async void NewImageKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (TryHandleApplicationShortcut(args, ViewModel.CanCreateImage))
        {
            await ShowNewImageDialogAsync();
        }
    }

    private async void PasteImageKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (TryHandleApplicationShortcut(args, ViewModel.CanCreateImage))
        {
            if (sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift))
                await PasteImageAndUpdateAsync(CancellationToken.None, asNewDocument: true);
            else
                await ShowPasteImageOptionsAsync();
        }
    }

    private async void CopyKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.HasCurrentDocument))
        {
            return;
        }

        await CopyCurrentDocumentAsync(CancellationToken.None);
    }

    private async void UndoKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanUndo))
        {
            return;
        }

        await UndoAndUpdateAsync(CancellationToken.None);
    }

    private async void RedoKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanRedo))
        {
            return;
        }

        await RedoAndUpdateAsync(CancellationToken.None);
    }

    private void FitCanvasKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanFitCanvas))
        {
            return;
        }

        FitCanvasAndUpdate();
    }

    private void ActualSizeKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.HasCurrentDocument))
        {
            return;
        }

        ActualSizeAndUpdate();
    }

    private void ZoomInKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanZoomIn))
        {
            return;
        }

        ZoomInAndUpdate();
    }

    private void ZoomOutKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanZoomOut))
        {
            return;
        }

        ZoomOutAndUpdate();
    }

    private async void AddAnnotationKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.HasCurrentDocument))
        {
            return;
        }

        await AddActiveAnnotationAndUpdateAsync(CancellationToken.None);
    }

    private async void DeleteAnnotationKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryHandleApplicationShortcut(args, ViewModel.CanDeleteSelectedAnnotation))
        {
            return;
        }

        await DeleteSelectedAnnotationAndUpdateAsync(CancellationToken.None);
    }

    private async void CaptureRegionButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CaptureAndFitAsync(CaptureMode.Region, CancellationToken.None);
    }

    private async void CapturePickerButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CaptureAndFitAsync(CaptureMode.Picker, CancellationToken.None);
    }

    private async void CaptureDisplayButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CaptureAndFitAsync(CaptureMode.Display, CancellationToken.None);
    }

    private async void CaptureFullScreenButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CaptureAndFitAsync(CaptureMode.FullScreen, CancellationToken.None);
    }

    private async void CaptureWindowButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CaptureAndFitAsync(CaptureMode.Window, CancellationToken.None);
    }

    private async void CaptureScrollingButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CaptureScrollingAndFitAsync(CancellationToken.None);
    }

    private async void ToggleScreenRecordingButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ToggleScreenRecordingAsync(CancellationToken.None);
    }

    private async void CaptureHotkey_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindings)
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            await ViewModel.UpdateCaptureHotkeyAsync(comboBox.SelectedIndex, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private void ScreenRecordingAudioMode_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            ViewModel.UpdateScreenRecordingAudioMode(comboBox.SelectedIndex);
            UpdateBindingsFromViewModel();
        }
    }

    private void SettingsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.OpenFirstRunSetup();
        UpdateBindingsFromViewModel();
    }

    private void FirstRunHotkey_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindings)
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            ViewModel.UpdateFirstRunCaptureHotkey(comboBox.SelectedIndex);
            UpdateBindingsFromViewModel();
        }
    }

    private void FirstRunStorageBackend_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindings)
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            ViewModel.UpdateFirstRunStorageBackend(comboBox.SelectedIndex);
            UpdateBindingsFromViewModel();
        }
    }

    private async void ChooseFirstRunStorageRootButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ChooseFirstRunStorageRootAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void CompleteFirstRunSetupButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.CompleteFirstRunSetupAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private void CloseSettingsButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.CloseSettingsSurface();
        UpdateBindingsFromViewModel();
    }

    private async void ImportSettingsButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ImportSettingsAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void ExportSettingsButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ExportSettingsAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void OpenPrivacyNoticeButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.OpenPrivacyNoticeAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private void HistorySearch_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchRecentDocuments(sender.Text);
        UpdateBindingsFromViewModel();
        ScrollCaptureHistoryToStart();
    }

    private void HistoryFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            ViewModel.UpdateHistoryFilter(comboBox.SelectedIndex);
            UpdateBindingsFromViewModel();
            ScrollCaptureHistoryToStart();
        }
    }

    private async void RecentDocuments_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindings || !ViewModel.CanManageCaptureHistory) return;
        if (sender is ListView { SelectedItem: DocumentSummaryItem item })
        {
            // Replacing a thumbnail must not reopen the current document and clear its undo history.
            if (item.Id == ViewModel.CurrentDocumentId?.ToString()) return;
            await ViewModel.OpenDocumentAsync(item.Id, CancellationToken.None);
            UpdateBindingsFromViewModel();
            FitCurrentCanvas();
        }
    }

    private void AnnotationLayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AnnotationLayerItem item })
        {
            ViewModel.SelectLayer(item.Id);
            UpdateBindingsFromViewModel();
        }
    }

    private async void RenameLayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_layerRenameDialogIsOpen || _layerColorDialogIsOpen || _newImageDialogIsOpen || _pasteImageDialogIsOpen
            || sender is not FrameworkElement { DataContext: AnnotationLayerItem item })
        {
            return;
        }

        _layerRenameDialogIsOpen = true;
        try
        {
            var name = new TextBox
            {
                Header = "Layer name",
                Text = item.Name,
                MaxLength = RenameAnnotationLayerCommand.MaximumNameLength,
                MinWidth = 280
            };
            AutomationProperties.SetName(name, "Layer name");
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Rename layer",
                Content = name,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text)
            };
            name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);
            dialog.Opened += (_, _) =>
            {
                name.Focus(FocusState.Programmatic);
                name.SelectAll();
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.RenameLayerAsync(item.Id, name.Text, CancellationToken.None);
                UpdateBindingsFromViewModel();
            }
        }
        finally
        {
            _layerRenameDialogIsOpen = false;
        }
    }

    private async void MoveLayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: AnnotationLayerItem item, Tag: string tag }
            && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int direction))
        {
            await ViewModel.MoveLayerAsync(item.Id, direction, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private async void LayerColorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: AnnotationLayerItem item, Tag: string tag }
            && Enum.TryParse(tag, out LayerColorLabel color) && Enum.IsDefined(color))
        {
            await ViewModel.SetLayerColorAsync(item.Id, color, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private async void LayerCustomColorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_layerColorDialogIsOpen || _layerRenameDialogIsOpen || _newImageDialogIsOpen || _pasteImageDialogIsOpen
            || sender is not FrameworkElement { DataContext: AnnotationLayerItem item })
        {
            return;
        }

        _layerColorDialogIsOpen = true;
        try
        {
            var picker = new ColorPicker
            {
                Color = item.IndicatorColor,
                IsAlphaEnabled = false,
                IsMoreButtonVisible = false,
                IsHexInputVisible = true
            };
            AutomationProperties.SetName(picker, "Custom layer color picker");
            AutomationProperties.SetHelpText(picker, $"Choose a color label for {item.Name}.");
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = $"Color for {item.Name}",
                Content = picker,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Color color = picker.Color;
                await ViewModel.SetLayerCustomColorAsync(item.Id,
                    new ColorRgba(color.R, color.G, color.B, 255), CancellationToken.None);
                UpdateBindingsFromViewModel();
            }
        }
        finally
        {
            _layerColorDialogIsOpen = false;
        }
    }

    private void AnnotationLayer_Expanding(Expander sender, ExpanderExpandingEventArgs e)
    {
        if (sender.DataContext is AnnotationLayerItem item)
        {
            ViewModel.SetLayerExpanded(item.Id, true);
        }
    }

    private void AnnotationLayer_Collapsed(Expander sender, ExpanderCollapsedEventArgs e)
    {
        if (sender.DataContext is AnnotationLayerItem item)
        {
            ViewModel.SetLayerExpanded(item.Id, false);
        }
    }

    private async void AnnotationLayerVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AnnotationLayerItem item })
        {
            await ViewModel.ToggleLayerVisibilityAsync(item.Id, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private void AnnotationObject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AnnotationOverlayItem item })
        {
            ViewModel.UseMoveTool();
            CanvasScrollViewer.Focus(FocusState.Programmatic);
            if (IsObjectModifierDown(VirtualKey.Shift) || IsObjectModifierDown(VirtualKey.Control)) ViewModel.ToggleAnnotationSelection(item.Id);
            else ViewModel.SelectAnnotation(item.Id);
            UpdateBindingsFromViewModel();
        }
    }

    private async void AnnotationObjectVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AnnotationOverlayItem item })
        {
            await ViewModel.ToggleAnnotationVisibilityAsync(item.Id, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private async void MoveSelectedObjectToLayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AnnotationLayerItem item })
        {
            await ViewModel.MoveSelectedAnnotationToLayerAsync(item.Id, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private void WorkspaceTitle_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingBindings)
        {
            return;
        }

        if (sender is TextBox textBox)
        {
            ViewModel.WorkspaceTitleText = textBox.Text;
            UpdateBindingsFromViewModel();
        }
    }

    private async void RenameCurrentDocumentButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.RenameCurrentDocumentAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void DuplicateCurrentDocumentButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.DuplicateCurrentDocumentAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async void OpenCurrentDocumentFolderButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.OpenCurrentDocumentFolderAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void DeleteCurrentDocumentButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await RunCaptureHistoryUiActionAsync(() => ViewModel.DeleteCurrentDocumentAsync(CancellationToken.None));
    }

    private async void MoveCurrentCaptureToTrashButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.CurrentDocumentId is { } id)
            await ConfirmDeleteCaptureImageAsync(id.ToString(), ViewModel.CurrentDocumentTitle);
    }

    private async void OpenButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await OpenAndFitAsync(CancellationToken.None);
    }

    private async void NewImageButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowNewImageDialogAsync();
    }

    private async void PasteImageButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowPasteImageOptionsAsync();
    }

    private async void PasteImageAsLayer_Click(object sender, RoutedEventArgs e)
    {
        await PasteImageAndUpdateAsync(CancellationToken.None);
    }

    private async void PasteImageAsNewDocument_Click(object sender, RoutedEventArgs e)
    {
        await PasteImageAndUpdateAsync(CancellationToken.None, asNewDocument: true);
    }

    private async Task ShowNewImageDialogAsync()
    {
        if (_newImageDialogIsOpen || _pasteImageDialogIsOpen || _layerRenameDialogIsOpen || _layerColorDialogIsOpen || !ViewModel.CanCreateImage)
        {
            return;
        }

        _newImageDialogIsOpen = true;
        try
        {
            var width = new NumberBox
            {
                Header = "Width (pixels)",
                Value = 1280,
                Minimum = 1,
                Maximum = NewImageRequest.MaximumDimension,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            var height = new NumberBox
            {
                Header = "Height (pixels)",
                Value = 720,
                Minimum = 1,
                Maximum = NewImageRequest.MaximumDimension,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            AutomationProperties.SetName(width, "New image width");
            AutomationProperties.SetName(height, "New image height");
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(errorText, AutomationLiveSetting.Polite);
            var content = new StackPanel { Spacing = 12, MinWidth = 280 };
            content.Children.Add(width);
            content.Children.Add(height);
            content.Children.Add(new TextBlock
            {
                Text = "The background will be transparent. Export as PNG to keep transparency.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(errorText);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "New image",
                Content = content,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            void ValidateSize()
            {
                string? error = NewImageRequest.GetValidationError(width.Value, height.Value);
                errorText.Text = error ?? string.Empty;
                errorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = error is null;
            }

            width.ValueChanged += (_, _) => ValidateSize();
            height.ValueChanged += (_, _) => ValidateSize();
            dialog.PrimaryButtonClick += (_, args) =>
            {
                ValidateSize();
                args.Cancel = !dialog.IsPrimaryButtonEnabled;
            };
            ValidateSize();
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.CreateBlankImageAsync(
                    new NewImageRequest((int)width.Value, (int)height.Value), CancellationToken.None);
                UpdateBindingsFromViewModel();
                FitCurrentCanvas();
            }
        }
        finally
        {
            _newImageDialogIsOpen = false;
        }
    }

    private async void CopyButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CopyCurrentDocumentAsync(CancellationToken.None);
    }

    private async void CopyRecognizedTextButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.CopyRecognizedTextAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void PinButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.PinCurrentDocumentAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void ExportPngButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ExportCurrentDocumentAsync(ExportFormat.Png, CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void ExportJpegButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ExportCurrentDocumentAsync(ExportFormat.Jpeg, CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void ExportPdfButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ExportCurrentDocumentAsync(ExportFormat.Pdf, CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private void CanvasScrollViewer_SizeChanged(
        object sender,
        Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        ViewModel.RefitCanvasIfNeeded(
            Math.Max(1, e.NewSize.Width - 24),
            Math.Max(1, e.NewSize.Height - 24));
        UpdateBindingsFromViewModel();
    }

    private void CanvasContent_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) || !ViewModel.HasCurrentDocument) return;
        var pointer = e.GetCurrentPoint(CanvasSurface);
        if (pointer.Properties.IsHorizontalMouseWheel || pointer.Properties.MouseWheelDelta == 0) return;
        // Handle this on the content before the event reaches ScrollViewer's scrolling behavior.
        e.Handled = true;
        if (_canvasDragMode != CanvasDragMode.None) return;

        double previousZoom = ViewModel.CanvasZoom;
        Point viewportPoint = e.GetCurrentPoint(CanvasScrollViewer).Position;
        Point sourcePoint = new(pointer.Position.X / previousZoom, pointer.Position.Y / previousZoom);
        ViewModel.ZoomWithWheel(pointer.Properties.MouseWheelDelta);
        if (ViewModel.CanvasZoom == previousZoom) return;

        UpdateBindingsFromViewModel();
        CanvasScrollViewer.UpdateLayout();
        Point movedPoint = CanvasSurface.TransformToVisual(CanvasScrollViewer).TransformPoint(
            new Point(sourcePoint.X * ViewModel.CanvasZoom, sourcePoint.Y * ViewModel.CanvasZoom));
        CanvasScrollViewer.ChangeView(
            Math.Clamp(CanvasScrollViewer.HorizontalOffset + movedPoint.X - viewportPoint.X, 0, CanvasScrollViewer.ScrollableWidth),
            Math.Clamp(CanvasScrollViewer.VerticalOffset + movedPoint.Y - viewportPoint.Y, 0, CanvasScrollViewer.ScrollableHeight),
            null, disableAnimation: true);
    }

    private void HistorySplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_historyPaneIsExpanded)
        {
            return;
        }

        _historySplitterIsDragging = true;
        _historySplitterStartPoint = e.GetCurrentPoint(MainContentGrid).Position;
        _historySplitterStartWidth = HistoryColumn.ActualWidth > 0
            ? HistoryColumn.ActualWidth
            : HistoryColumn.Width.Value;
        HistorySplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void HistorySplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_historySplitterIsDragging)
        {
            return;
        }

        Point currentPoint = e.GetCurrentPoint(MainContentGrid).Position;
        ResizeHistoryPane(_historySplitterStartWidth
            + (currentPoint.X - _historySplitterStartPoint.X));
        e.Handled = true;
    }

    private void HistorySplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndHistorySplitterDrag();
        e.Handled = true;
    }

    private void HistorySplitter_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndHistorySplitterDrag();
        e.Handled = true;
    }

    private void EndHistorySplitterDrag()
    {
        if (!_historySplitterIsDragging)
        {
            return;
        }

        _historySplitterIsDragging = false;
        HistorySplitter.ReleasePointerCaptures();
    }

    private void ResizeHistoryPane(double requestedWidth)
    {
        double resizedWidth = Math.Clamp(
            requestedWidth,
            HistoryMinimumWidth,
            GetHistoryMaximumWidth());

        _historyExpandedWidth = resizedWidth;
        HistoryColumn.Width = new GridLength(resizedWidth);
    }

    private void HistoryPanePinButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CollapseHistoryPane();
    }

    private void HistoryPaneExpandButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ExpandHistoryPane();
    }

    private void CollapseHistoryPane()
    {
        if (!_historyPaneIsExpanded)
        {
            return;
        }

        EndHistorySplitterDrag();
        if (HistoryColumn.ActualWidth >= HistoryMinimumWidth)
        {
            _historyExpandedWidth = Math.Clamp(
                HistoryColumn.ActualWidth,
                HistoryMinimumWidth,
                GetHistoryMaximumWidth());
        }

        _historyPaneIsExpanded = false;
        HistoryExpandedView.Visibility = Visibility.Collapsed;
        HistoryCollapsedView.Visibility = Visibility.Visible;
        HistorySplitter.Visibility = Visibility.Collapsed;
        HistorySplitterColumn.Width = new GridLength(0);
        HistoryColumn.MinWidth = HistoryCollapsedWidth;
        HistoryColumn.Width = new GridLength(HistoryCollapsedWidth);
    }

    private void ExpandHistoryPane()
    {
        if (_historyPaneIsExpanded)
        {
            return;
        }

        _historyPaneIsExpanded = true;
        HistoryColumn.MinWidth = HistoryMinimumWidth;
        HistorySplitterColumn.Width = new GridLength(HistorySplitterWidth);
        HistorySplitter.Visibility = Visibility.Visible;

        double restoredWidth = Math.Clamp(
            _historyExpandedWidth,
            HistoryMinimumWidth,
            GetHistoryMaximumWidth());
        HistoryColumn.Width = new GridLength(restoredWidth);
        HistoryCollapsedView.Visibility = Visibility.Collapsed;
        HistoryExpandedView.Visibility = Visibility.Visible;
    }

    private double GetHistoryMaximumWidth()
    {
        double rightWidth = RightControlsColumn.ActualWidth > 0
            ? RightControlsColumn.ActualWidth
            : RightControlsColumn.Width.Value;
        double availableWidth = MainContentGrid.ActualWidth
            - rightWidth
            - HistorySplitterColumn.Width.Value
            - RightControlsSplitterColumn.Width.Value
            - EditorMinimumWidth;

        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return HistoryMaximumWidth;
        }

        return Math.Max(
            HistoryMinimumWidth,
            Math.Min(HistoryMaximumWidth, availableWidth));
    }

    private void RightControlsSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightControlsPaneIsExpanded)
        {
            return;
        }

        _rightControlsSplitterIsDragging = true;
        _rightControlsSplitterStartPoint = e.GetCurrentPoint(MainContentGrid).Position;
        _rightControlsSplitterStartWidth = RightControlsColumn.ActualWidth > 0
            ? RightControlsColumn.ActualWidth
            : RightControlsColumn.Width.Value;
        RightControlsSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RightControlsSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightControlsSplitterIsDragging)
        {
            return;
        }

        Point currentPoint = e.GetCurrentPoint(MainContentGrid).Position;
        ResizeRightControlsPane(_rightControlsSplitterStartWidth
            - (currentPoint.X - _rightControlsSplitterStartPoint.X));
        e.Handled = true;
    }

    private void RightControlsSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndRightControlsSplitterDrag();
        e.Handled = true;
    }

    private void RightControlsSplitter_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndRightControlsSplitterDrag();
        e.Handled = true;
    }

    private void EndRightControlsSplitterDrag()
    {
        if (!_rightControlsSplitterIsDragging)
        {
            return;
        }

        _rightControlsSplitterIsDragging = false;
        RightControlsSplitter.ReleasePointerCaptures();
    }

    private void ResizeRightControlsPane(double requestedWidth)
    {
        double resizedWidth = Math.Clamp(
            requestedWidth,
            RightControlsMinimumWidth,
            GetRightControlsMaximumWidth());

        _rightControlsExpandedWidth = resizedWidth;
        RightControlsColumn.Width = new GridLength(resizedWidth);
    }

    private void RightControlsPanePinButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CollapseRightControlsPane();
    }

    private void RightControlsPaneExpandButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ExpandRightControlsPane();
    }

    private void CollapseRightControlsPane()
    {
        if (!_rightControlsPaneIsExpanded)
        {
            return;
        }

        if (RightControlsColumn.ActualWidth >= RightControlsMinimumWidth)
        {
            _rightControlsExpandedWidth = Math.Clamp(
                RightControlsColumn.ActualWidth,
                RightControlsMinimumWidth,
                GetRightControlsMaximumWidth());
        }

        _rightControlsPaneIsExpanded = false;
        RightControlsExpandedView.Visibility = Visibility.Collapsed;
        RightControlsCollapsedView.Visibility = Visibility.Visible;
        RightControlsSplitter.Visibility = Visibility.Collapsed;
        RightControlsSplitterColumn.Width = new GridLength(0);
        RightControlsColumn.MinWidth = RightControlsCollapsedWidth;
        RightControlsColumn.Width = new GridLength(RightControlsCollapsedWidth);
    }

    private void ExpandRightControlsPane()
    {
        if (_rightControlsPaneIsExpanded)
        {
            return;
        }

        _rightControlsPaneIsExpanded = true;
        RightControlsColumn.MinWidth = RightControlsMinimumWidth;
        RightControlsSplitterColumn.Width = new GridLength(RightControlsSplitterWidth);
        RightControlsSplitter.Visibility = Visibility.Visible;

        double restoredWidth = Math.Clamp(
            _rightControlsExpandedWidth,
            RightControlsMinimumWidth,
            GetRightControlsMaximumWidth());
        RightControlsColumn.Width = new GridLength(restoredWidth);
        RightControlsCollapsedView.Visibility = Visibility.Collapsed;
        RightControlsExpandedView.Visibility = Visibility.Visible;
    }

    private double GetRightControlsMaximumWidth()
    {
        double historyWidth = HistoryColumn.ActualWidth > 0
            ? HistoryColumn.ActualWidth
            : HistoryColumn.Width.Value;
        double availableWidth = MainContentGrid.ActualWidth
            - historyWidth
            - HistorySplitterColumn.Width.Value
            - RightControlsSplitterColumn.Width.Value
            - EditorMinimumWidth;

        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return RightControlsMaximumWidth;
        }

        return Math.Max(
            RightControlsMinimumWidth,
            Math.Min(RightControlsMaximumWidth, availableWidth));
    }

    private void FitCanvasButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FitCanvasAndUpdate();
    }

    private void ZoomOutButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ZoomOutAndUpdate();
    }

    private void ZoomInButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ZoomInAndUpdate();
    }

    private void ActualSizeButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ActualSizeAndUpdate();
    }

    private async void AddAnnotationButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await AddActiveAnnotationAndUpdateAsync(CancellationToken.None);
    }

    private async void AddLayerButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.AddLayerAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void DeleteLayer_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Guid layerId;
        string layerName;
        int objectCount;
        if (sender is FrameworkElement { DataContext: AnnotationLayerItem item })
        {
            layerId = item.Id;
            if (!ViewModel.TryGetLayerForRemoval(layerId, out layerName, out objectCount))
            {
                return;
            }
        }
        else if (!ViewModel.TryGetActiveLayerForRemoval(out layerId, out layerName, out objectCount))
        {
            return;
        }

        if (objectCount > 0)
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = $"Delete {layerName}?",
                Content = $"This layer contains {objectCount} object{(objectCount == 1 ? string.Empty : "s")}. "
                    + "Deleting the layer also removes its objects. You can undo this action.",
                PrimaryButtonText = "Delete layer",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await ViewModel.DeleteLayerAsync(layerId, CancellationToken.None);
        UpdateBindingsFromViewModel();
    }

    private async void DeleteAnnotationButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await DeleteSelectedAnnotationAndUpdateAsync(CancellationToken.None);
    }

    private async void CropButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.CropToSelectedAnnotationAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async void ResizeButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ResizeCurrentDocumentAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private void AnnotationToolButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.Tag is not string tag
            || !int.TryParse(tag, out int toolIndex))
        {
            return;
        }

        SelectCanvasTool(toolIndex);
        AnnotationKind kind = DesignKinds[toolIndex];
        if (ViewModel.GetToolDesigns(kind).Any()) ShowToolDesignDrawer(button, kind);
    }

    private void AnnotationPreset_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            ViewModel.UpdateAnnotationPreset(comboBox.SelectedIndex);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void AnnotationStroke_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            if (comboBox.SelectedIndex == ViewModel.AnnotationStrokeIndex) return;
            await ViewModel.UpdateAnnotationStrokeAsync(
                comboBox.SelectedIndex,
                CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void AnnotationStrokeButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.Tag is not string tag
            || !int.TryParse(tag, out int strokeIndex))
        {
            return;
        }

        await ViewModel.UpdateAnnotationStrokeAsync(strokeIndex, CancellationToken.None);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
    }

    private async void AnnotationStrokeSize_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is Slider && Math.Abs(e.NewValue - ViewModel.AnnotationSizeSliderValue) >= 0.01)
        {
            await ViewModel.UpdateAnnotationStrokeThicknessAsync(
                e.NewValue,
                CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void AnnotationSizeText_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        // TextChanged can arrive after a binding refresh has left the synchronous guard.
        if (ShouldIgnoreControlEvent() || sender is not TextBox textBox || textBox.Text == ViewModel.AnnotationSizeText)
        {
            return;
        }

        await ViewModel.UpdateAnnotationSizeTextAsync(
            textBox.Text,
            CancellationToken.None);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
    }

    private void AnnotationSizeText_LostFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetAnnotationSizeText();
    }

    private async void AnnotationCornerStyle_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            if (comboBox.SelectedIndex == ViewModel.AnnotationCornerStyleIndex) return;
            await ViewModel.UpdateAnnotationCornerStyleAsync(
                comboBox.SelectedIndex,
                CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void AnnotationCustomColor_ColorChanged(
        ColorPicker sender,
        ColorChangedEventArgs args)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        Color color = args.NewColor;
        if (color == WorkspaceAnnotationColor) return;
        await ViewModel.UpdateAnnotationCustomColorAsync(
            color.R,
            color.G,
            color.B,
            color.A,
            CancellationToken.None);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
    }

    private async void AnnotationOpacity_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is Slider && Math.Abs(e.NewValue - ViewModel.AnnotationOpacity) >= 0.001)
        {
            await ViewModel.UpdateAnnotationOpacityAsync(
                e.NewValue,
                CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void AnnotationText_LostFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            await ViewModel.UpdateAnnotationTextAsync(textBox.Text, CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void UndoButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await UndoAndUpdateAsync(CancellationToken.None);
    }

    private async void RedoButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await RedoAndUpdateAsync(CancellationToken.None);
    }

    private bool IsPenToolActive => !ViewModel.IsSelectionToolActive && ViewModel.ActiveAnnotationKind == AnnotationKind.Pen;

    private bool IsFillToolActive => !ViewModel.IsSelectionToolActive && ViewModel.ActiveAnnotationKind == AnnotationKind.Fill;

    private void SelectionToolButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAreaSelectionTools();
    }

    private void MoveToolButton_Click(object sender, RoutedEventArgs e) => SelectCanvasTool(-1);

    private void AnnotationOverlay_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (!ViewModel.IsMoveToolActive) return;
        if (sender is Microsoft.UI.Xaml.FrameworkElement
            {
                DataContext: AnnotationOverlayItem item
            })
        {
            // PointerPressed owns selection, so the trailing tap cannot collapse a group or toggle twice.
            e.Handled = true;
        }
    }

    private void AnnotationOverlays_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        RefreshAnnotationCanvas();
    }

    private void AnnotationBody_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsMoveToolActive) return;
        if (sender is FrameworkElement { DataContext: AnnotationOverlayItem item })
        {
            if (e.GetCurrentPoint(CanvasSurface).Properties.IsLeftButtonPressed
                && (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)))
            {
                ViewModel.ToggleAnnotationSelection(item.Id);
                CanvasScrollViewer.Focus(FocusState.Programmatic);
                e.Handled = true;
                return;
            }
            BeginAnnotationDrag(e, item, AnnotationBoundsHandle.Move);
        }
    }

    private void AnnotationHandle_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsMoveToolActive) return;
        if (sender is not FrameworkElement
            {
                DataContext: AnnotationOverlayItem item,
                Tag: string handleName
            }
            || !Enum.TryParse(handleName, out AnnotationBoundsHandle handle))
        {
            return;
        }

        BeginAnnotationDrag(e, item, handle);
    }

    private async void CanvasSurface_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_polygonSelectionFinishing) { e.Handled = true; return; }
        if (_canvasDragMode == CanvasDragMode.SelectPolygon)
        {
            e.Handled = true;
            var polygonPoint = e.GetCurrentPoint(CanvasSurface);
            if (polygonPoint.Properties.IsLeftButtonPressed) await AddPolygonCornerAsync(ClampToCanvas(polygonPoint.Position));
            return;
        }
        if (_canvasDragMode != CanvasDragMode.None
            || !ViewModel.HasCurrentDocument)
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint pointerPoint = e.GetCurrentPoint(CanvasSurface);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        CanvasScrollViewer.Focus(FocusState.Programmatic);
        if (ViewModel.IsSelectionToolActive)
        {
            _marqueeOriginalSelection = ViewModel.SelectedAnnotationIds.ToArray();
            _marqueeOriginalBounds = ViewModel.MarqueeSourceBounds;
            _marqueeOriginalPixelMask = ViewModel.SourcePixelSelection;
            _marqueeSelectionMode = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) && ViewModel.HasCanvasSelection ? AnnotationSelectionMode.Add
                : e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ? AnnotationSelectionMode.Toggle : AnnotationSelectionMode.Replace;
            _annotationDragStart = ClampToCanvas(pointerPoint.Position);
            _pixelDragMode = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) && e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu) ? PixelSelectionMode.Intersect
                : e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu) ? PixelSelectionMode.Subtract
                : e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) && ViewModel.SourcePixelSelection is not null ? PixelSelectionMode.Add : ViewModel.AreaSelectionMode;
            if (ViewModel.IsMarqueeToolActive && ViewModel.AreaSelectionTool == PixelSelectionTool.PolygonalLasso)
            {
                BeginPolygonSelection(_annotationDragStart);
                e.Handled = true;
                return;
            }
            if (ViewModel.IsMarqueeToolActive && ViewModel.AreaSelectionTool == PixelSelectionTool.MagicWand)
            {
                e.Handled = true;
                await ViewModel.SelectSimilarPixelsAsync(new(pointerPoint.Position.X, pointerPoint.Position.Y), _pixelDragMode, CancellationToken.None);
                UpdateBindingsFromViewModel();
                return;
            }
            if (ViewModel.IsMarqueeToolActive && _pixelDragMode == PixelSelectionMode.Replace
                && ViewModel.AreaSelectionTool is PixelSelectionTool.Rectangle or PixelSelectionTool.Ellipse
                && _marqueeOriginalBounds is RectD sourceArea
                && (_marqueeOriginalPixelMask is null || _marqueeOriginalPixelMask.Contains((int)(_annotationDragStart.X / ViewModel.CanvasZoom), (int)(_annotationDragStart.Y / ViewModel.CanvasZoom)))
                && new Rect(sourceArea.X * ViewModel.CanvasZoom, sourceArea.Y * ViewModel.CanvasZoom, sourceArea.Width * ViewModel.CanvasZoom, sourceArea.Height * ViewModel.CanvasZoom).Contains(_annotationDragStart))
            {
                _canvasDragMode = CanvasDragMode.MoveMarquee;
                CanvasSurface.CapturePointer(e.Pointer);
                e.Handled = true;
                return;
            }
            if (!ViewModel.IsMarqueeToolActive && _marqueeSelectionMode == AnnotationSelectionMode.Replace) ViewModel.ClearAnnotationSelection();
            _marqueeLastPointer = _annotationDragStart;
            _marqueePreviewBounds = null;
            _lassoSelectionPoints.Clear();
            _canvasDragMode = CanvasDragMode.SelectObjects;
            CanvasSurface.CapturePointer(e.Pointer);
            ShowSelectionMarquee(_annotationDragStart, e.KeyModifiers);
            e.Handled = true;
            return;
        }

        ViewModel.ClearAnnotationSelection();
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();

        if (IsFillToolActive)
        {
            e.Handled = true;
            await ViewModel.FillAtCanvasPointAsync(new PointD(pointerPoint.Position.X, pointerPoint.Position.Y), CancellationToken.None);
            UpdateBindingsFromViewModel();
            return;
        }

        if (IsPenToolActive)
        {
            _canvasDragMode = CanvasDragMode.DrawPen;
            _penStrokePoints.Clear();
            PenStrokePreview.Points.Clear();
            AnnotationStyle style = ViewModel.DrawingStyle;
            var brush = new SolidColorBrush(Color.FromArgb(style.Stroke.A, style.Stroke.R, style.Stroke.G, style.Stroke.B));
            PenStrokePreview.Stroke = brush;
            PenStrokePreview.StrokeThickness = style.StrokeThickness * ViewModel.CanvasZoom;
            PenStrokePreview.Opacity = style.Opacity;
            PenDotPreview.Fill = brush;
            PenDotPreview.Opacity = style.Opacity;
            PenDotPreview.Width = PenDotPreview.Height = PenStrokePreview.StrokeThickness;
            PenStrokePreview.Visibility = Visibility.Visible;
            AppendPenPoint(pointerPoint.Position);
            CanvasSurface.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        _canvasDragMode = CanvasDragMode.DrawAnnotation;
        _annotationDragStart = ClampToCanvas(pointerPoint.Position);
        CanvasSurface.CapturePointer(e.Pointer);
        ShowDrawingPreview(_annotationDragStart);
        e.Handled = true;
    }

    private void CanvasSurface_PointerMoved(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_canvasDragMode == CanvasDragMode.None)
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint pointerPoint = e.GetCurrentPoint(CanvasSurface);
        Point currentPoint = ClampToCanvas(pointerPoint.Position);

        if (_canvasDragMode == CanvasDragMode.SelectPolygon)
        {
            if (ViewModel.CreateSourcePointFromCanvasPoint(new(currentPoint.X, currentPoint.Y)) is { } source) ShowPolygonSelectionPreview(source);
            e.Handled = true;
            return;
        }

        if (_canvasDragMode == CanvasDragMode.SelectObjects)
        {
            if (ViewModel.IsMarqueeToolActive && ViewModel.AreaSelectionTool == PixelSelectionTool.Lasso)
                foreach (var sample in e.GetIntermediatePoints(CanvasSurface).OrderBy(point => point.Timestamp))
                    AppendLassoSelectionPoint(ClampToCanvas(sample.Position));
            ShowSelectionMarquee(currentPoint, e.KeyModifiers);
        }
        else if (_canvasDragMode == CanvasDragMode.MoveMarquee) ShowMovingMarquee(currentPoint);
        else if (_canvasDragMode == CanvasDragMode.DrawPen)
        {
            foreach (Microsoft.UI.Input.PointerPoint sample in e.GetIntermediatePoints(CanvasSurface).OrderBy(point => point.Timestamp))
            {
                AppendPenPoint(sample.Position);
            }
        }
        else if (_canvasDragMode == CanvasDragMode.DrawAnnotation)
        {
            Point previewPoint = CreateConstrainedShapePointIfNeeded(currentPoint, e);
            ShowDrawingPreview(previewPoint);
        }
        else
        {
            ShowAnnotationEditPreview(currentPoint, e);
        }

        e.Handled = true;
    }

    private async void CanvasSurface_PointerReleased(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_canvasDragMode == CanvasDragMode.SelectPolygon) { e.Handled = true; return; }
        if (_canvasDragMode == CanvasDragMode.None)
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint pointerPoint = e.GetCurrentPoint(CanvasSurface);
        Point currentPoint = ClampToCanvas(pointerPoint.Position);

        if (_canvasDragMode == CanvasDragMode.SelectObjects)
        {
            ShowSelectionMarquee(currentPoint, e.KeyModifiers);
            RectD marqueeBounds = _marqueePreviewBounds ?? default;
            AnnotationSelectionMode mode = _marqueeSelectionMode;
            bool areaSelection = ViewModel.IsMarqueeToolActive;
            PointD[] points = _lassoSelectionPoints.ToArray();
            EndCanvasDrag(e);
            if (areaSelection)
                await ViewModel.SelectPixelAreaAsync(marqueeBounds, points, _pixelDragMode, CancellationToken.None);
            else if (marqueeBounds.Width >= ViewModel.CanvasZoom && marqueeBounds.Height >= ViewModel.CanvasZoom)
                ViewModel.SelectAnnotationsInCanvasBounds(marqueeBounds, mode);
            else if (mode != AnnotationSelectionMode.Replace) ViewModel.RestoreAnnotationSelection(_marqueeOriginalSelection, _marqueeOriginalBounds, _marqueeOriginalPixelMask);
            UpdateBindingsFromViewModel();
            return;
        }

        if (_canvasDragMode == CanvasDragMode.MoveMarquee)
        {
            double dx = (currentPoint.X - _annotationDragStart.X) / ViewModel.CanvasZoom;
            double dy = (currentPoint.Y - _annotationDragStart.Y) / ViewModel.CanvasZoom;
            EndCanvasDrag(e);
            ViewModel.MoveMarquee(dx, dy);
            UpdateBindingsFromViewModel();
            return;
        }

        if (_canvasDragMode == CanvasDragMode.DrawPen)
        {
            AppendPenPoint(currentPoint);
            PointD[] points = _penStrokePoints.ToArray();
            EndCanvasDrag(e);
            await ViewModel.AddPenStrokeAsync(points, CancellationToken.None);
            UpdateBindingsFromViewModel();
            return;
        }

        if (_canvasDragMode is CanvasDragMode.MoveAnnotation or CanvasDragMode.ResizeAnnotation)
        {
            await CompleteAnnotationDragAsync(currentPoint, e);
            return;
        }

        Point annotationEndPoint = CreateConstrainedShapePointIfNeeded(currentPoint, e);
        RectD displayBounds = CreateDisplayBounds(
            _annotationDragStart,
            annotationEndPoint);
        EndCanvasDrag(e);

        bool isLinearAnnotation = IsLinearAnnotationKind(ViewModel.ActiveAnnotationKind);
        bool isTooSmall = !isLinearAnnotation
            ? displayBounds.Width < MinimumDragDisplaySize
                || displayBounds.Height < MinimumDragDisplaySize
            : CalculateDistance(_annotationDragStart, currentPoint) < MinimumDragDisplaySize;

        if (isTooSmall)
        {
            ViewModel.ClearAnnotationSelection();
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
            return;
        }

        RectD? sourceBounds = isLinearAnnotation
            ? ViewModel.CreateDirectedSourceBoundsFromCanvasDrag(
                new PointD(_annotationDragStart.X, _annotationDragStart.Y),
                new PointD(currentPoint.X, currentPoint.Y))
            : ViewModel.CreateSourceBoundsFromCanvasDrag(displayBounds);
        if (sourceBounds is RectD bounds)
        {
            await ViewModel.AddActiveAnnotationAsync(bounds, CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private void CanvasSurface_PointerCanceled(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_canvasDragMode == CanvasDragMode.SelectPolygon) { CancelPolygonSelection(); return; }
        if (_canvasDragMode != CanvasDragMode.None)
        {
            if (_canvasDragMode is CanvasDragMode.SelectObjects or CanvasDragMode.MoveMarquee) ViewModel.RestoreAnnotationSelection(_marqueeOriginalSelection, _marqueeOriginalBounds, _marqueeOriginalPixelMask);
            EndCanvasDrag(e);
        }
    }

    private void CanvasSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_canvasDragMode == CanvasDragMode.SelectPolygon) return;
        if (_canvasDragMode != CanvasDragMode.None)
        {
            if (_canvasDragMode is CanvasDragMode.SelectObjects or CanvasDragMode.MoveMarquee) ViewModel.RestoreAnnotationSelection(_marqueeOriginalSelection, _marqueeOriginalBounds, _marqueeOriginalPixelMask);
            EndCanvasDrag(e);
        }
    }

    private void AppendPenPoint(Point point)
    {
        Point display = ClampToCanvas(point);
        PointD? source = ViewModel.CreateSourcePointFromCanvasPoint(new PointD(display.X, display.Y));
        if (source is not PointD sample) return;
        if (_penStrokePoints.Count > 0)
        {
            PointD last = _penStrokePoints[^1];
            if (Math.Abs(last.X - sample.X) * ViewModel.CanvasZoom < 0.5
                && Math.Abs(last.Y - sample.Y) * ViewModel.CanvasZoom < 0.5) return;
        }
        _penStrokePoints.Add(sample);
        PenStrokePreview.Points.Add(display);
        PenDotPreview.Visibility = _penStrokePoints.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(PenDotPreview, display.X - PenDotPreview.Width / 2);
        Canvas.SetTop(PenDotPreview, display.Y - PenDotPreview.Height / 2);
    }

    private async Task CaptureAndFitAsync(CaptureMode captureMode, CancellationToken cancellationToken)
    {
        await ViewModel.CaptureAsync(captureMode, cancellationToken);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async Task CaptureScrollingAndFitAsync(CancellationToken cancellationToken)
    {
        await ViewModel.CaptureScrollingAsync(cancellationToken);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async Task ToggleScreenRecordingAsync(CancellationToken cancellationToken)
    {
        await ViewModel.ToggleScreenRecordingAsync(cancellationToken);
        UpdateBindingsFromViewModel();
    }

    private async Task OpenAndFitAsync(CancellationToken cancellationToken)
    {
        await ViewModel.OpenImageFileAsync(cancellationToken);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async Task PasteImageAndUpdateAsync(CancellationToken cancellationToken, bool asNewDocument = false)
    {
        var previousDocumentId = ViewModel.CurrentDocumentId;
        if (asNewDocument)
            await ViewModel.PasteImageAsNewDocumentAsync(cancellationToken);
        else
            await ViewModel.PasteImageAsync(cancellationToken);
        UpdateBindingsFromViewModel();
        if (ViewModel.CurrentDocumentId != previousDocumentId)
            FitCurrentCanvas();
        else if (ViewModel.HasSelectedAnnotation && !ShouldIgnoreApplicationShortcut())
            CanvasScrollViewer.Focus(FocusState.Programmatic);
    }

    private async Task CopyCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        await ViewModel.CopyCurrentDocumentAsync(cancellationToken);
        UpdateBindingsFromViewModel();
    }

    private async Task AddActiveAnnotationAndUpdateAsync(CancellationToken cancellationToken)
    {
        await ViewModel.AddActiveAnnotationAsync(cancellationToken);
        UpdateBindingsFromViewModel();
    }

    private async Task DeleteSelectedAnnotationAndUpdateAsync(CancellationToken cancellationToken)
    {
        await ViewModel.DeleteSelectedAnnotationAsync(cancellationToken);
        UpdateBindingsFromViewModel();
    }

    private async Task UndoAndUpdateAsync(CancellationToken cancellationToken)
    {
        await ViewModel.UndoAsync(cancellationToken);
        UpdateBindingsFromViewModel();
    }

    private async Task RedoAndUpdateAsync(CancellationToken cancellationToken)
    {
        await ViewModel.RedoAsync(cancellationToken);
        UpdateBindingsFromViewModel();
    }

    private void FitCanvasAndUpdate()
    {
        FitCurrentCanvas();
        UpdateBindingsFromViewModel();
    }

    private void ActualSizeAndUpdate()
    {
        ViewModel.ResetZoom();
        UpdateBindingsFromViewModel();
    }

    private void ZoomInAndUpdate()
    {
        ViewModel.ZoomIn();
        UpdateBindingsFromViewModel();
    }

    private void ZoomOutAndUpdate()
    {
        ViewModel.ZoomOut();
        UpdateBindingsFromViewModel();
    }

    private bool TryHandleApplicationShortcut(
        KeyboardAcceleratorInvokedEventArgs args,
        bool canExecute = true)
    {
        if (!canExecute || ShouldIgnoreApplicationShortcut())
        {
            return false;
        }

        args.Handled = true;
        return true;
    }

    private bool ShouldIgnoreApplicationShortcut()
    {
        return !_isReadyForControlEvents
            || _newImageDialogIsOpen
            || _pasteImageDialogIsOpen
            || _layerRenameDialogIsOpen
            || _layerColorDialogIsOpen
            || _historyFolderDialogIsOpen
            || _captureDeleteDialogIsOpen
            || _captureRenameDialogIsOpen
            || _captureTagsDialogIsOpen
            || _toolDesignDialogIsOpen
            || _selectionFillDialogIsOpen
            || ViewModel.FirstRunSetupIsOpen
            || IsEditableFocusedElement(FocusManager.GetFocusedElement(XamlRoot));
    }

    private static bool IsEditableFocusedElement(object? focusedElement)
    {
        for (DependencyObject? element = focusedElement as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or ComboBox or NumberBox or Slider or ContentDialog)
                return true;
        }
        return false;
    }

    private void FitCurrentCanvas()
    {
        ViewModel.FitCanvasToViewport(
            Math.Max(1, CanvasScrollViewer.ActualWidth - 24),
            Math.Max(1, CanvasScrollViewer.ActualHeight - 24));
    }

    private void SubscribeAnnotationOverlays(ShellViewModel viewModel)
    {
        if (_annotationOverlayViewModel is not null)
        {
            _annotationOverlayViewModel.AnnotationOverlays.CollectionChanged -=
                AnnotationOverlays_CollectionChanged;
        }

        _annotationOverlayViewModel = viewModel;
        viewModel.AnnotationOverlays.CollectionChanged += AnnotationOverlays_CollectionChanged;
    }

    private void RefreshAnnotationCanvas()
    {
        SynchronizeAnnotationCanvas();
    }

    private FrameworkElement CreateAnnotationElement(AnnotationOverlayItem item)
    {
        FrameworkElement element = item.Kind switch
        {
            AnnotationKind.Line or AnnotationKind.Arrow => CreateLinearAnnotationElement(item),
            AnnotationKind.Text => CreateTextAnnotationElement(item),
            AnnotationKind.Blur => CreateBlurAnnotationElement(item),
            AnnotationKind.Ellipse => CreateEllipseAnnotationElement(item),
            AnnotationKind.Pen => CreatePenAnnotationElement(item),
            AnnotationKind.Fill => CreateFillAnnotationElement(item),
            AnnotationKind.Image or AnnotationKind.Vector => CreateImageAnnotationElement(item),
            _ => CreateRectangleAnnotationElement(item)
        };

        if (item.ShowTransformHandles && item.IsSelected && ViewModel.SelectedAnnotationCount > 1 && element is Panel panel)
        {
            panel.Children.Add(CreateSelectionOutline(item));
        }
        ConfigureAnnotationAutomation(element, item);
        return element;
    }

    private static void ConfigureAnnotationAutomation(
        FrameworkElement element,
        AnnotationOverlayItem item)
    {
        AutomationProperties.SetName(element, CreateAnnotationAutomationName(item));
        AutomationProperties.SetHelpText(
            element,
            "Annotation overlay. Use V to select, move, or resize this object. M selects an area.");
    }

    private static string CreateAnnotationAutomationName(AnnotationOverlayItem item)
    {
        string selectedPrefix = item.IsSelected ? "Selected " : string.Empty;
        string kind = item.Kind switch
        {
            AnnotationKind.Arrow => "arrow",
            AnnotationKind.Line => "line",
            AnnotationKind.Rectangle => "rectangle",
            AnnotationKind.Ellipse => "ellipse",
            AnnotationKind.Text => $"text, {TrimAutomationText(item.Text)}",
            AnnotationKind.Highlight => "highlight",
            AnnotationKind.Blur => "blur",
            AnnotationKind.Pen => "pen stroke",
            AnnotationKind.Fill => "filled region",
            AnnotationKind.Image => TrimAutomationText(item.Text),
            AnnotationKind.Vector => "SVG shape, " + item.LayerLabel,
            _ => "annotation"
        };
        return $"{selectedPrefix}{kind} annotation, {Math.Round(item.Width)} by {Math.Round(item.Height)}";
    }

    private static string TrimAutomationText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "empty";
        }

        string trimmed = text.Trim();
        return trimmed.Length <= 32
            ? trimmed
            : trimmed.Substring(0, 32) + "...";
    }

    private FrameworkElement CreateRectangleAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid
        {
            Width = item.Width,
            Height = item.Height,
            DataContext = item
        };

        var body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = item.StrokeThickness,
            CornerRadius = new CornerRadius(item.CornerRadius),
            Margin = item.Kind == AnnotationKind.Rectangle ? new Thickness(-item.StrokeThicknessValue / 2) : new Thickness(0),
            DataContext = item
        };
        body.PointerPressed += AnnotationBody_PointerPressed;
        body.Tapped += AnnotationOverlay_Tapped;
        root.Children.Add(body);

        root.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Fill = item.FillBrush,
            Stroke = item.StrokeBrush,
            StrokeThickness = item.StrokeThicknessValue,
            StrokeDashArray = item.StrokeDashes,
            RadiusX = item.CornerRadius,
            RadiusY = item.CornerRadius,
            Margin = item.Kind == AnnotationKind.Rectangle ? new Thickness(-item.StrokeThicknessValue / 2) : new Thickness(0),
            IsHitTestVisible = false
        });

        AddBoxResizeHandles(root, item);
        return root;
    }

    private void AddBoxResizeHandles(Grid root, AnnotationOverlayItem item)
    {
        if (item.ShowTransformHandles && item.IsSelected && ViewModel.SelectedAnnotationCount == 1)
        {
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Top,
                new Thickness(-5, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Top,
                HorizontalAlignment.Center,
                VerticalAlignment.Top,
                new Thickness(0, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0, -5, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Right,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0, 0, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, -5, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Bottom,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Bottom,
                new Thickness(-5, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Left,
                HorizontalAlignment.Left,
                VerticalAlignment.Center,
                new Thickness(-5, 0, 0, 0),
                item));
        }

    }

    private FrameworkElement CreateImageAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid { Width = item.Width, Height = item.Height, DataContext = item };
        // Reuse the decoded pixels across drag previews, selection changes, and overlay refreshes.
        // Weak keys let images leave the cache when their document and undo history release them.
        var bitmap = item.Image is null ? null : _annotationImageSources.GetValue(item.Image, static image =>
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(image.PngBytes, writable: false);
            bitmap.SetSource(stream.AsRandomAccessStream());
            return bitmap;
        });
        var body = new Grid { Width = item.Width, Height = item.Height, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), DataContext = item };
        body.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = bitmap, Stretch = Stretch.Fill, Opacity = item.Opacity, IsHitTestVisible = false });
        body.PointerPressed += AnnotationBody_PointerPressed;
        body.Tapped += AnnotationOverlay_Tapped;
        body.SetBinding(UIElement.IsHitTestVisibleProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(ShellViewModel.IsSelectionToolActive)),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
        });
        var drawing = new Canvas { Clip = new RectangleGeometry { Rect = new Rect(-item.Left, -item.Top, ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight) } };
        drawing.Children.Add(body);
        root.Children.Add(drawing);
        AddBoxResizeHandles(root, item);
        return root;
    }

    private FrameworkElement CreateFillAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid { Width = item.Width, Height = item.Height, DataContext = item };
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (RectD rectangle in item.FillRectangles)
            geometry.Children.Add(new RectangleGeometry { Rect = new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) });
        var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = item.FillBrush, DataContext = item };
        // A filled background must allow drawing other tools over it.
        path.SetBinding(UIElement.IsHitTestVisibleProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = ViewModel,
            Path = new PropertyPath(nameof(ShellViewModel.IsSelectionToolActive)),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
        });
        path.PointerPressed += AnnotationBody_PointerPressed;
        path.Tapped += AnnotationOverlay_Tapped;
        var drawing = new Canvas
        {
            Clip = new RectangleGeometry { Rect = new Rect(-item.Left, -item.Top, ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight) }
        };
        drawing.Children.Add(path);
        root.Children.Add(drawing);
        AddBoxResizeHandles(root, item);
        return root;
    }

    private FrameworkElement CreatePenAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid { Width = item.Width, Height = item.Height, DataContext = item };
        var drawing = new Canvas
        {
            Clip = new RectangleGeometry { Rect = new Rect(-item.Left, -item.Top, ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight) }
        };
        var points = new PointCollection();
        foreach (PointD point in item.StrokePoints) points.Add(new Point(point.X, point.Y));
        var hit = new Microsoft.UI.Xaml.Shapes.Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            StrokeThickness = Math.Max(12, item.StrokeThicknessValue),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            DataContext = item
        };
        hit.PointerPressed += AnnotationBody_PointerPressed;
        hit.Tapped += AnnotationOverlay_Tapped;
        drawing.Children.Add(hit);
        if (points.Count == 1)
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = item.StrokeThicknessValue,
                Height = item.StrokeThicknessValue,
                Fill = item.StrokeBrush,
                DataContext = item
            };
            Canvas.SetLeft(dot, points[0].X - dot.Width / 2);
            Canvas.SetTop(dot, points[0].Y - dot.Height / 2);
            dot.PointerPressed += AnnotationBody_PointerPressed;
            dot.Tapped += AnnotationOverlay_Tapped;
            drawing.Children.Add(dot);
        }
        else
        {
            drawing.Children.Add(new Microsoft.UI.Xaml.Shapes.Polyline
            {
                Points = CreatePenPointCollection(item.StrokePoints),
                Stroke = item.StrokeBrush,
                StrokeDashArray = item.StrokeDashes,
                StrokeThickness = item.StrokeThicknessValue,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            });
        }
        root.Children.Add(drawing);
        AddBoxResizeHandles(root, item);
        return root;
    }

    private static PointCollection CreatePenPointCollection(IReadOnlyList<PointD> points)
    {
        var collection = new PointCollection();
        foreach (PointD point in points) collection.Add(new Point(point.X, point.Y));
        return collection;
    }

    private FrameworkElement CreateEllipseAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid
        {
            Width = item.Width,
            Height = item.Height,
            DataContext = item
        };

        var hitTarget = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Margin = new Thickness(-item.StrokeThicknessValue / 2),
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Stroke = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            StrokeThickness = Math.Max(12, item.StrokeThicknessValue + 8),
            DataContext = item
        };
        hitTarget.PointerPressed += AnnotationBody_PointerPressed;
        hitTarget.Tapped += AnnotationOverlay_Tapped;
        root.Children.Add(hitTarget);

        root.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Margin = new Thickness(-item.StrokeThicknessValue / 2),
            Fill = item.FillBrush,
            Stroke = item.StrokeBrush,
            StrokeThickness = item.StrokeThicknessValue,
            StrokeDashArray = item.StrokeDashes,
            IsHitTestVisible = false
        });

        if (item.ShowTransformHandles && item.IsSelected && ViewModel.SelectedAnnotationCount == 1)
        {
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Top,
                new Thickness(-5, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Top,
                HorizontalAlignment.Center,
                VerticalAlignment.Top,
                new Thickness(0, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0, -5, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Right,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0, 0, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, -5, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Bottom,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Bottom,
                new Thickness(-5, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Left,
                HorizontalAlignment.Left,
                VerticalAlignment.Center,
                new Thickness(-5, 0, 0, 0),
                item));
        }

        return root;
    }

    private FrameworkElement CreateBlurAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid
        {
            Width = item.Width,
            Height = item.Height,
            Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, item.Width, item.Height)
            },
            DataContext = item
        };

        var blurHost = new Grid
        {
            Width = item.Width,
            Height = item.Height,
            IsHitTestVisible = false
        };
        blurHost.Loaded += (_, _) => ApplyBackdropBlur(blurHost, item);
        blurHost.SizeChanged += (_, _) => ApplyBackdropBlur(blurHost, item);
        root.Children.Add(blurHost);

        var hitTarget = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            BorderBrush = item.ShowTransformHandles && item.IsSelected
                ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = item.ShowTransformHandles && item.IsSelected ? new Thickness(1) : new Thickness(0),
            DataContext = item
        };
        hitTarget.PointerPressed += AnnotationBody_PointerPressed;
        hitTarget.Tapped += AnnotationOverlay_Tapped;
        root.Children.Add(hitTarget);

        if (item.ShowTransformHandles && item.IsSelected && ViewModel.SelectedAnnotationCount == 1)
        {
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Top,
                new Thickness(-5, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Top,
                HorizontalAlignment.Center,
                VerticalAlignment.Top,
                new Thickness(0, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0, -5, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Right,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0, 0, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, -5, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Bottom,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Bottom,
                new Thickness(-5, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Left,
                HorizontalAlignment.Left,
                VerticalAlignment.Center,
                new Thickness(-5, 0, 0, 0),
                item));
        }

        return root;
    }

    private FrameworkElement CreateTextAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Grid
        {
            Width = item.Width,
            Height = item.Height,
            DataContext = item
        };

        var body = new Border
        {
            Background = item.FillBrush,
            DataContext = item,
            Padding = new Thickness(SnapStudio.Rendering.TextAnnotationLayout.Padding * ViewModel.CanvasZoom)
        };
        body.PointerPressed += AnnotationBody_PointerPressed;
        body.Tapped += AnnotationOverlay_Tapped;

        body.Child = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = Microsoft.UI.Xaml.Controls.StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = item.Text,
                Foreground = item.TextBrush,
                FontFamily = new FontFamily(item.FontFamily),
                FontSize = item.FontSize,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                IsTextSelectionEnabled = false,
                IsHitTestVisible = false
            }
        };

        root.Children.Add(body);

        if (item.ShowTransformHandles && item.IsSelected && ViewModel.SelectedAnnotationCount == 1)
        {
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Top,
                new Thickness(-5, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Top,
                HorizontalAlignment.Center,
                VerticalAlignment.Top,
                new Thickness(0, -5, 0, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.TopRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0, -5, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Right,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0, 0, -5, 0),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomRight,
                HorizontalAlignment.Right,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, -5, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Bottom,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom,
                new Thickness(0, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.BottomLeft,
                HorizontalAlignment.Left,
                VerticalAlignment.Bottom,
                new Thickness(-5, 0, 0, -5),
                item));
            root.Children.Add(CreateResizeHandle(
                AnnotationBoundsHandle.Left,
                HorizontalAlignment.Left,
                VerticalAlignment.Center,
                new Thickness(-5, 0, 0, 0),
                item));
        }

        return root;
    }

    private FrameworkElement CreateLinearAnnotationElement(AnnotationOverlayItem item)
    {
        var root = new Canvas
        {
            Width = item.Width,
            Height = item.Height,
            DataContext = item
        };

        var hitLine = new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = item.StartX,
            Y1 = item.StartY,
            X2 = item.EndX,
            Y2 = item.EndY,
            Stroke = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            StrokeThickness = Math.Max(12, item.StrokeThicknessValue + 8),
            DataContext = item
        };
        hitLine.PointerPressed += AnnotationBody_PointerPressed;
        hitLine.Tapped += AnnotationOverlay_Tapped;
        root.Children.Add(hitLine);

        if (item.Kind == AnnotationKind.Arrow)
        {
            root.Children.Add(CreateArrowElement(item));
        }
        else
        {
            root.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = item.StartX,
                Y1 = item.StartY,
                X2 = item.EndX,
                Y2 = item.EndY,
                Stroke = item.StrokeBrush,
                StrokeThickness = item.StrokeThicknessValue,
                StrokeDashArray = item.StrokeDashes,
                IsHitTestVisible = false
            });
        }

        if (item.ShowTransformHandles && item.IsSelected && ViewModel.SelectedAnnotationCount == 1)
        {
            root.Children.Add(CreateEndpointMarker(
                item.StartX,
                item.StartY,
                AnnotationBoundsHandle.Start,
                item));
            root.Children.Add(CreateEndpointMarker(
                item.EndX,
                item.EndY,
                AnnotationBoundsHandle.End,
                item));
        }

        return root;
    }

    private FrameworkElement CreateArrowElement(AnnotationOverlayItem item)
    {
        var points = new PointCollection();
        foreach (PointD point in ArrowGeometry.CreateOutline(
            new PointD(item.StartX, item.StartY), new PointD(item.EndX, item.EndY),
            item.StrokeThicknessValue, ViewModel.CanvasZoom))
        {
            points.Add(new Point(point.X, point.Y));
        }

        if (item.Pattern != StrokePattern.Solid && points.Count == 7)
        {
            var drawing = new Canvas { IsHitTestVisible = false };
            Point neck = new((points[2].X + points[4].X) / 2, (points[2].Y + points[4].Y) / 2);
            drawing.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = item.StartX,
                Y1 = item.StartY,
                X2 = neck.X,
                Y2 = neck.Y,
                Stroke = item.StrokeBrush,
                StrokeThickness = item.StrokeThicknessValue,
                StrokeDashArray = item.StrokeDashes
            });
            drawing.Children.Add(new Microsoft.UI.Xaml.Shapes.Polygon { Points = [points[2], points[3], points[4]], Fill = item.StrokeBrush });
            return drawing;
        }
        return new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Points = points,
            Fill = item.StrokeBrush,
            IsHitTestVisible = false
        };
    }

    private Border CreateEndpointMarker(
        double centerX,
        double centerY,
        AnnotationBoundsHandle handle,
        AnnotationOverlayItem item)
    {
        var marker = new Border
        {
            Width = 12,
            Height = 12,
            Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            DataContext = item,
            Tag = handle.ToString()
        };
        marker.PointerPressed += AnnotationHandle_PointerPressed;
        AutomationProperties.SetName(
            marker,
            $"{FormatAnnotationBoundsHandle(handle)} handle for {CreateAnnotationAutomationName(item)}");
        AutomationProperties.SetHelpText(marker, "Drag this handle to resize the selected annotation.");

        Canvas.SetLeft(marker, centerX - marker.Width / 2);
        Canvas.SetTop(marker, centerY - marker.Height / 2);

        return marker;
    }

    private Border CreateResizeHandle(
        AnnotationBoundsHandle handle,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment,
        Thickness margin,
        AnnotationOverlayItem item)
    {
        var resizeHandle = new Border
        {
            Width = 10,
            Height = 10,
            Margin = margin,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
            BorderThickness = new Thickness(1),
            DataContext = item,
            Tag = handle.ToString()
        };
        resizeHandle.PointerPressed += AnnotationHandle_PointerPressed;
        AutomationProperties.SetName(
            resizeHandle,
            $"{FormatAnnotationBoundsHandle(handle)} resize handle for {CreateAnnotationAutomationName(item)}");
        AutomationProperties.SetHelpText(resizeHandle, "Drag this handle to resize the selected annotation.");

        return resizeHandle;
    }

    private static string FormatAnnotationBoundsHandle(AnnotationBoundsHandle handle)
    {
        return handle switch
        {
            AnnotationBoundsHandle.TopLeft => "Top left",
            AnnotationBoundsHandle.Top => "Top",
            AnnotationBoundsHandle.TopRight => "Top right",
            AnnotationBoundsHandle.Right => "Right",
            AnnotationBoundsHandle.BottomRight => "Bottom right",
            AnnotationBoundsHandle.Bottom => "Bottom",
            AnnotationBoundsHandle.BottomLeft => "Bottom left",
            AnnotationBoundsHandle.Left => "Left",
            AnnotationBoundsHandle.Start => "Start",
            AnnotationBoundsHandle.End => "End",
            _ => "Move"
        };
    }

    private void BeginAnnotationDrag(
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e,
        AnnotationOverlayItem item,
        AnnotationBoundsHandle handle)
    {
        if (_canvasDragMode != CanvasDragMode.None)
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint pointerPoint = e.GetCurrentPoint(CanvasSurface);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point displayPoint = ClampToCanvas(pointerPoint.Position);
        PointD? sourcePoint = ViewModel.CreateSourcePointFromCanvasPoint(
            new PointD(displayPoint.X, displayPoint.Y));
        RectD? sourceBounds = ViewModel.GetAnnotationBounds(item.Id);
        if (sourcePoint is not PointD dragStartSourcePoint
            || sourceBounds is not RectD originalSourceBounds)
        {
            return;
        }

        CanvasScrollViewer.Focus(FocusState.Programmatic);
        if (!ViewModel.SelectedAnnotationIds.Contains(item.Id) || handle != AnnotationBoundsHandle.Move)
            ViewModel.SelectAnnotation(item.Id);
        UpdateBindingsFromViewModel();

        _canvasDragMode = handle == AnnotationBoundsHandle.Move
            ? CanvasDragMode.MoveAnnotation
            : CanvasDragMode.ResizeAnnotation;
        _annotationDragId = item.Id;
        _annotationDragHandle = handle;
        _annotationDragOriginalSourceBounds = originalSourceBounds;
        _annotationDragStartSourcePoint = dragStartSourcePoint;
        _annotationDragStart = displayPoint;
        _annotationDragHasMoved = false;
        _annotationDragBounds = ViewModel.GetSelectedAnnotationBounds();
        _annotationDragMarqueeBounds = ViewModel.PersistentSelectionSourceBounds;
        CanvasSurface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ShowAnnotationEditPreview(
        Point currentDisplayPoint,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_annotationDragHasMoved)
        {
            if (CalculateDistance(_annotationDragStart, currentDisplayPoint) < MinimumDragDisplaySize) return;
            _annotationDragHasMoved = true;
            SetDraggedAnnotationVisibility(Visibility.Collapsed);
        }
        IReadOnlyDictionary<Guid, RectD> bounds = CreateSelectionDragBounds(currentDisplayPoint, IsShiftPressed(e));
        AnnotationDragPreview.Children.Clear();
        foreach (var pair in bounds)
            AddDragAnnotationPreview(ViewModel.CreateAnnotationEditPreview(pair.Key, pair.Value), bounds.Count > 1);
        ShowMovedSelectionMarquee(bounds);
    }

    private async Task CompleteAnnotationDragAsync(
        Point currentDisplayPoint,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ShowAnnotationEditPreview(currentDisplayPoint, e);
        if (!_annotationDragHasMoved) { EndCanvasDrag(e); UpdateBindingsFromViewModel(); return; }
        IReadOnlyDictionary<Guid, RectD> editedBounds = CreateSelectionDragBounds(currentDisplayPoint, IsShiftPressed(e));
        string displayName = _annotationDragHandle == AnnotationBoundsHandle.Move
            ? "Move Annotation"
            : "Resize Annotation";

        EndCanvasDrag(e);

        if (editedBounds.Count > 0)
        {
            await ViewModel.UpdateSelectionBoundsAsync(
                editedBounds,
                displayName,
                CancellationToken.None);
            UpdateBindingsFromViewModel();
        }
    }

    private RectD? CreateAnnotationDragBounds(
        Point currentDisplayPoint,
        bool constrainRectangleOrEllipse)
    {
        PointD? currentSourcePoint = ViewModel.CreateSourcePointFromCanvasPoint(
            new PointD(currentDisplayPoint.X, currentDisplayPoint.Y));

        return currentSourcePoint is PointD sourcePoint
            ? ViewModel.CreateEditedAnnotationBounds(
                _annotationDragHandle,
                _annotationDragOriginalSourceBounds,
                _annotationDragStartSourcePoint,
                sourcePoint,
                constrainRectangleOrEllipse)
            : null;
    }

    private void EndCanvasDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs? e = null)
    {
        _polygonalLasso.Clear();
        _polygonDocumentId = null;
        _polygonSourceImage = null;
        SetDraggedAnnotationVisibility(Visibility.Visible);
        _canvasDragMode = CanvasDragMode.None;
        _annotationDragId = Guid.Empty;
        _annotationDragHasMoved = false;
        _marqueePreviewBounds = null;
        _annotationDragHandle = AnnotationBoundsHandle.Move;
        _annotationDragOriginalSourceBounds = default;
        _annotationDragStartSourcePoint = default;
        _annotationDragBounds = new Dictionary<Guid, RectD>();
        if (e is not null) CanvasSurface.ReleasePointerCapture(e.Pointer);
        else CanvasSurface.ReleasePointerCaptures();
        AnnotationDragPreview.Children.Clear();
        RefreshPersistentSelectionMarquee();
        PenStrokePreview.Visibility = Visibility.Collapsed;
        PenDotPreview.Visibility = Visibility.Collapsed;
        PenStrokePreview.Points.Clear();
        _penStrokePoints.Clear();
        if (e is not null) e.Handled = true;
    }

    private void SetDraggedAnnotationVisibility(Visibility visibility)
    {
        foreach (FrameworkElement element in AnnotationCanvas.Children.OfType<FrameworkElement>())
        {
            if (element.DataContext is AnnotationOverlayItem item && _annotationDragBounds.ContainsKey(item.Id))
            {
                element.Visibility = visibility;
            }
        }
    }

    private void ShowDrawingPreview(Point end)
    {
        ShowDragAnnotationPreview(ViewModel.CreateActiveAnnotationPreview(
            new PointD(_annotationDragStart.X, _annotationDragStart.Y), new PointD(end.X, end.Y)));
    }

    private void ShowDragAnnotationPreview(AnnotationOverlayItem? item)
    {
        AnnotationDragPreview.Children.Clear();
        AddDragAnnotationPreview(item);
    }

    private void AddDragAnnotationPreview(AnnotationOverlayItem? item, bool showSelectionOutline = false)
    {
        if (item is null)
        {
            return;
        }

        FrameworkElement preview = CreateAnnotationElement(item);
        preview.IsHitTestVisible = false;
        AutomationProperties.SetName(preview, $"Preview of {CreateAnnotationAutomationName(item)}");
        AutomationProperties.SetHelpText(preview, "Shows the annotation while drawing, moving, or resizing it.");
        Canvas.SetLeft(preview, item.Left);
        Canvas.SetTop(preview, item.Top);
        AnnotationDragPreview.Children.Add(preview);
        if (showSelectionOutline)
        {
            var selectionOutline = CreateSelectionOutline(item);
            Canvas.SetLeft(selectionOutline, item.Left);
            Canvas.SetTop(selectionOutline, item.Top);
            AnnotationDragPreview.Children.Add(selectionOutline);
        }

        if (item.Kind == AnnotationKind.Blur)
        {
            var outline = new Border
            {
                Width = item.Width,
                Height = item.Height,
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(outline, item.Left);
            Canvas.SetTop(outline, item.Top);
            AnnotationDragPreview.Children.Add(outline);
        }
    }

    private Point CreateConstrainedShapePointIfNeeded(
        Point currentPoint,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!IsShiftPressed(e)
            || ViewModel.ActiveAnnotationKind is not (AnnotationKind.Rectangle or AnnotationKind.Ellipse))
        {
            return currentPoint;
        }

        double deltaX = currentPoint.X - _annotationDragStart.X;
        double deltaY = currentPoint.Y - _annotationDragStart.Y;
        double side = Math.Min(Math.Abs(deltaX), Math.Abs(deltaY));
        return ClampToCanvas(new Point(
            _annotationDragStart.X + Math.Sign(deltaX) * side,
            _annotationDragStart.Y + Math.Sign(deltaY) * side));
    }

    private void UpdateBindingsFromViewModel()
    {
        if (_canvasDragMode == CanvasDragMode.SelectPolygon && (_polygonDocumentId != ViewModel.CurrentDocumentId
            || !ReferenceEquals(_polygonSourceImage, ViewModel.CanvasImageSource) || !ViewModel.IsMarqueeToolActive
            || ViewModel.AreaSelectionTool != PixelSelectionTool.PolygonalLasso)) CancelPolygonSelection();
        _isUpdatingBindings = true;
        try
        {
            Bindings.Update();
            CaptureHistoryList.SelectedItem = ViewModel.RecentDocuments.FirstOrDefault(
                item => item.Id == ViewModel.CurrentDocumentId?.ToString());
            NotifyWorkspacePresentationChanged();
        }
        finally
        {
            _isUpdatingBindings = false;
        }

        UpdateAnnotationToolbarSelection();
        RefreshPersistentSelectionMarquee();
    }

    private void UpdateAnnotationToolbarSelection()
    {
        UpdateToolDesignBadges();
        UpdateToolButtonAppearance(SelectionToolButton, ViewModel.IsMarqueeToolActive);
        UpdateToolButtonAppearance(MoveToolButton, ViewModel.IsMoveToolActive);
        CanvasSurface.UpdateCursor(ViewModel.IsMarqueeToolActive, ViewModel.IsMoveToolActive);
        UpdateAnnotationToolButton(RectangleToolButton, 0);
        UpdateAnnotationToolButton(EllipseToolButton, 1);
        UpdateAnnotationToolButton(LineToolButton, 2);
        UpdateAnnotationToolButton(ArrowToolButton, 3);
        UpdateAnnotationToolButton(TextToolButton, 4);
        UpdateAnnotationToolButton(HighlightToolButton, 5);
        UpdateAnnotationToolButton(BlurToolButton, 6);
        UpdateAnnotationToolButton(PenToolButton, 7);
        UpdateAnnotationToolButton(FillToolButton, 8);
        UpdateAnnotationToolButton(CustomShapeToolButton, 9);
    }

    private void UpdateAnnotationToolButton(Button button, int toolIndex)
    {
        bool isActive = !ViewModel.IsSelectionToolActive && ViewModel.AnnotationToolIndex == toolIndex;
        UpdateToolButtonAppearance(button, isActive);
    }

    private void UpdateToolButtonAppearance(Button button, bool isActive)
    {
        button.Background = isActive
            ? (Brush)Resources["SnapAccentMutedBrush"]
            : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        button.BorderBrush = isActive
            ? (Brush)Resources["WorkspaceLayerSelectedBorderBrush"]
            : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        button.Foreground = isActive
            ? (Brush)Resources["SnapAccentBrush"]
            : (Brush)Resources["WorkspaceSecondaryTextBrush"];
    }

    private bool ShouldIgnoreControlEvent()
    {
        return !_isReadyForControlEvents || _isUpdatingBindings;
    }

    private static void ApplyBackdropBlur(
        FrameworkElement host,
        AnnotationOverlayItem item)
    {
        if (host.ActualWidth <= 0 || host.ActualHeight <= 0)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(host);
        Compositor compositor = visual.Compositor;
        var blurEffect = new GaussianBlurEffect
        {
            BlurAmount = ResolveBlurAmount(item),
            BorderMode = EffectBorderMode.Hard,
            Source = new CompositionEffectSourceParameter("Backdrop")
        };
        CompositionEffectFactory effectFactory = compositor.CreateEffectFactory(blurEffect);
        CompositionEffectBrush effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());

        SpriteVisual spriteVisual = compositor.CreateSpriteVisual();
        spriteVisual.Brush = effectBrush;
        spriteVisual.Size = new Vector2(
            (float)host.ActualWidth,
            (float)host.ActualHeight);

        ElementCompositionPreview.SetElementChildVisual(host, spriteVisual);
    }

    private static float ResolveBlurAmount(AnnotationOverlayItem item)
    {
        return (float)Math.Clamp(item.Opacity * 32, 8, 32);
    }

    private Point ClampToCanvas(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, Math.Max(0, CanvasSurface.ActualWidth)),
            Math.Clamp(point.Y, 0, Math.Max(0, CanvasSurface.ActualHeight)));
    }

    private static RectD CreateDisplayBounds(Point start, Point end)
    {
        double left = Math.Min(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        double right = Math.Max(start.X, end.X);
        double bottom = Math.Max(start.Y, end.Y);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static double CalculateDistance(Point start, Point end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static bool IsShiftPressed(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        return e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
    }

    private static bool IsLinearAnnotationKind(AnnotationKind annotationKind)
    {
        return annotationKind is AnnotationKind.Line or AnnotationKind.Arrow;
    }

}
