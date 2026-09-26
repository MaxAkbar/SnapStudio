using System.Collections.Specialized;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml.Automation;
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
    private const double RightControlsCollapsedWidth = 52;
    private const double RightControlsExpandedDefaultWidth = 280;
    private const double RightControlsMinimumWidth = 238;
    private const double RightControlsMaximumWidth = 560;
    private const double RightControlsSplitterWidth = 10;
    private const double EditorMinimumWidth = 360;

    private CanvasDragMode _canvasDragMode;
    private Guid _annotationDragId;
    private AnnotationBoundsHandle _annotationDragHandle = AnnotationBoundsHandle.Move;
    private RectD _annotationDragOriginalSourceBounds;
    private PointD _annotationDragStartSourcePoint;
    private Point _rectangleDragStart;
    private bool _isUpdatingBindings;
    private bool _isReadyForControlEvents;
    private bool _rightControlsPaneIsExpanded = true;
    private bool _rightControlsSplitterIsDragging;
    private Point _rightControlsSplitterStartPoint;
    private double _rightControlsSplitterStartWidth;
    private double _rightControlsExpandedWidth = RightControlsExpandedDefaultWidth;
    private ShellViewModel? _annotationOverlayViewModel;

    public MainPage()
    {
        InitializeComponent();
    }

    public ShellViewModel ViewModel { get; private set; } = ShellViewModel.Empty;

    private enum CanvasDragMode
    {
        None,
        DrawAnnotation,
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
            DispatcherQueue);
        SubscribeAnnotationOverlays(ViewModel);
        ViewModel.PropertyChanged += (_, _) =>
        {
            UpdateBindingsFromViewModel();
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
        }
    }

    private async void RecentDocuments_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: DocumentSummaryItem item })
        {
            await ViewModel.OpenDocumentAsync(item.Id, CancellationToken.None);
            UpdateBindingsFromViewModel();
            FitCurrentCanvas();
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
        await ViewModel.DeleteCurrentDocumentAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async void MoveCurrentCaptureToTrashButton_Click(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.MoveCurrentCaptureToTrashAsync(CancellationToken.None);
        UpdateBindingsFromViewModel();
        FitCurrentCanvas();
    }

    private async void OpenButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await OpenAndFitAsync(CancellationToken.None);
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
        double splitterWidth = RightControlsSplitterColumn.ActualWidth > 0
            ? RightControlsSplitterColumn.ActualWidth
            : RightControlsSplitterWidth;
        double availableWidth = MainContentGrid.ActualWidth
            - HistoryColumn.ActualWidth
            - splitterWidth
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

    private void AnnotationTool_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent())
        {
            return;
        }

        if (sender is ComboBox { SelectedIndex: >= 0 } comboBox)
        {
            ViewModel.UpdateAnnotationTool(comboBox.SelectedIndex);
            UpdateBindingsFromViewModel();
        }
    }

    private void AnnotationToolButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.Tag is not string tag
            || !int.TryParse(tag, out int toolIndex))
        {
            return;
        }

        ViewModel.UpdateAnnotationTool(toolIndex);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
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

        if (sender is Slider)
        {
            await ViewModel.UpdateAnnotationStrokeThicknessAsync(
                e.NewValue,
                CancellationToken.None);
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
        }
    }

    private async void AnnotationSizeNumber_ValueChanged(
        object sender,
        NumberBoxValueChangedEventArgs e)
    {
        if (ShouldIgnoreControlEvent()
            || double.IsNaN(e.NewValue))
        {
            return;
        }

        await ViewModel.UpdateAnnotationStrokeThicknessAsync(
            e.NewValue,
            CancellationToken.None);
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();
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

        if (sender is Slider)
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

    private void AnnotationOverlay_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement
            {
                DataContext: AnnotationOverlayItem item
            })
        {
            ViewModel.SelectAnnotation(item.Id);
            UpdateBindingsFromViewModel();
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
        if (sender is FrameworkElement { DataContext: AnnotationOverlayItem item })
        {
            BeginAnnotationDrag(e, item, AnnotationBoundsHandle.Move);
        }
    }

    private void AnnotationHandle_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
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

    private void CanvasSurface_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_canvasDragMode != CanvasDragMode.None
            || !ViewModel.HasCurrentDocument
            || IsAnnotationInteraction(e.OriginalSource))
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint pointerPoint = e.GetCurrentPoint(CanvasSurface);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        ViewModel.ClearAnnotationSelection();
        UpdateBindingsFromViewModel();
        RefreshAnnotationCanvas();

        _canvasDragMode = CanvasDragMode.DrawAnnotation;
        _rectangleDragStart = ClampToCanvas(pointerPoint.Position);
        CanvasSurface.CapturePointer(e.Pointer);
        ShowDragRectanglePreview(CreateDisplayBounds(_rectangleDragStart, _rectangleDragStart));
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

        if (_canvasDragMode == CanvasDragMode.DrawAnnotation)
        {
            Point previewPoint = CreateConstrainedShapePointIfNeeded(currentPoint, e);
            ShowDragRectanglePreview(CreateDisplayBounds(_rectangleDragStart, previewPoint));
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
        if (_canvasDragMode == CanvasDragMode.None)
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint pointerPoint = e.GetCurrentPoint(CanvasSurface);
        Point currentPoint = ClampToCanvas(pointerPoint.Position);

        if (_canvasDragMode is CanvasDragMode.MoveAnnotation or CanvasDragMode.ResizeAnnotation)
        {
            await CompleteAnnotationDragAsync(currentPoint, e);
            return;
        }

        Point annotationEndPoint = CreateConstrainedShapePointIfNeeded(currentPoint, e);
        RectD displayBounds = CreateDisplayBounds(
            _rectangleDragStart,
            annotationEndPoint);
        EndCanvasDrag(e);

        bool isLinearAnnotation = IsLinearAnnotationKind(ViewModel.ActiveAnnotationKind);
        bool isTooSmall = !isLinearAnnotation
            ? displayBounds.Width < MinimumDragDisplaySize
                || displayBounds.Height < MinimumDragDisplaySize
            : CalculateDistance(_rectangleDragStart, currentPoint) < MinimumDragDisplaySize;

        if (isTooSmall)
        {
            ViewModel.ClearAnnotationSelection();
            UpdateBindingsFromViewModel();
            RefreshAnnotationCanvas();
            return;
        }

        RectD? sourceBounds = isLinearAnnotation
            ? ViewModel.CreateDirectedSourceBoundsFromCanvasDrag(
                new PointD(_rectangleDragStart.X, _rectangleDragStart.Y),
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
        if (_canvasDragMode != CanvasDragMode.None)
        {
            EndCanvasDrag(e);
        }
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
            || ViewModel.FirstRunSetupIsOpen
            || IsEditableFocusedElement(FocusManager.GetFocusedElement(XamlRoot));
    }

    private static bool IsEditableFocusedElement(object? focusedElement)
    {
        return focusedElement is TextBox
            or PasswordBox
            or RichEditBox
            or AutoSuggestBox
            or ComboBox
            or NumberBox
            or Slider;
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
        AnnotationCanvas.Children.Clear();

        foreach (AnnotationOverlayItem item in ViewModel.AnnotationOverlays)
        {
            FrameworkElement annotationElement = CreateAnnotationElement(item);
            Canvas.SetLeft(annotationElement, item.Left);
            Canvas.SetTop(annotationElement, item.Top);
            AnnotationCanvas.Children.Add(annotationElement);
        }
    }

    private FrameworkElement CreateAnnotationElement(AnnotationOverlayItem item)
    {
        FrameworkElement element = item.Kind switch
        {
            AnnotationKind.Line or AnnotationKind.Arrow => CreateLinearAnnotationElement(item),
            AnnotationKind.Text => CreateTextAnnotationElement(item),
            AnnotationKind.Blur => CreateBlurAnnotationElement(item),
            AnnotationKind.Ellipse => CreateEllipseAnnotationElement(item),
            _ => CreateRectangleAnnotationElement(item)
        };

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
            "Annotation overlay. Use the pointer to select, move, or resize it.");
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
            Background = item.FillBrush,
            BorderBrush = item.StrokeBrush,
            BorderThickness = item.StrokeThickness,
            CornerRadius = new CornerRadius(item.CornerRadius),
            DataContext = item
        };
        body.PointerPressed += AnnotationBody_PointerPressed;
        body.Tapped += AnnotationOverlay_Tapped;
        root.Children.Add(body);

        if (item.IsSelected)
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
            Fill = item.FillBrush,
            Stroke = item.StrokeBrush,
            StrokeThickness = item.StrokeThicknessValue,
            IsHitTestVisible = false
        });

        if (item.IsSelected)
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
            BorderBrush = item.IsSelected
                ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = item.IsSelected ? new Thickness(1) : new Thickness(0),
            DataContext = item
        };
        hitTarget.PointerPressed += AnnotationBody_PointerPressed;
        hitTarget.Tapped += AnnotationOverlay_Tapped;
        root.Children.Add(hitTarget);

        if (item.IsSelected)
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
            BorderBrush = item.StrokeBrush,
            BorderThickness = item.StrokeThickness,
            DataContext = item,
            Padding = new Thickness(4)
        };
        body.PointerPressed += AnnotationBody_PointerPressed;
        body.Tapped += AnnotationOverlay_Tapped;

        body.Child = new TextBlock
        {
            Text = item.Text,
            Foreground = item.TextBrush,
            FontSize = item.FontSize,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };

        root.Children.Add(body);

        if (item.IsSelected)
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

        root.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = item.StartX,
            Y1 = item.StartY,
            X2 = item.EndX,
            Y2 = item.EndY,
            Stroke = item.StrokeBrush,
            StrokeThickness = item.StrokeThicknessValue,
            IsHitTestVisible = false
        });

        if (item.Kind == AnnotationKind.Arrow)
        {
            root.Children.Add(CreateArrowHead(item));
        }

        if (item.IsSelected)
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

    private Microsoft.UI.Xaml.Shapes.Polygon CreateArrowHead(AnnotationOverlayItem item)
    {
        const double minimumHeadLength = 10;
        double deltaX = item.EndX - item.StartX;
        double deltaY = item.EndY - item.StartY;
        double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length < 1)
        {
            return new Microsoft.UI.Xaml.Shapes.Polygon();
        }

        double unitX = deltaX / length;
        double unitY = deltaY / length;
        double perpendicularX = -unitY;
        double perpendicularY = unitX;
        double headLength = Math.Max(minimumHeadLength, item.StrokeThicknessValue * 4);
        double headWidth = Math.Max(8, item.StrokeThicknessValue * 3);
        var tip = new Point(item.EndX, item.EndY);
        var basePoint = new Point(
            tip.X - unitX * headLength,
            tip.Y - unitY * headLength);
        var left = new Point(
            basePoint.X + perpendicularX * headWidth / 2,
            basePoint.Y + perpendicularY * headWidth / 2);
        var right = new Point(
            basePoint.X - perpendicularX * headWidth / 2,
            basePoint.Y - perpendicularY * headWidth / 2);

        return new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Points = new PointCollection
            {
                tip,
                left,
                right
            },
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

        ViewModel.SelectAnnotation(item.Id);
        UpdateBindingsFromViewModel();

        _canvasDragMode = handle == AnnotationBoundsHandle.Move
            ? CanvasDragMode.MoveAnnotation
            : CanvasDragMode.ResizeAnnotation;
        _annotationDragId = item.Id;
        _annotationDragHandle = handle;
        _annotationDragOriginalSourceBounds = originalSourceBounds;
        _annotationDragStartSourcePoint = dragStartSourcePoint;
        CanvasSurface.CapturePointer(e.Pointer);
        ShowDragRectanglePreview(ViewModel.CreateDisplayBoundsFromSourceBounds(originalSourceBounds));
        e.Handled = true;
    }

    private void ShowAnnotationEditPreview(
        Point currentDisplayPoint,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        RectD? editedSourceBounds = CreateAnnotationDragBounds(
            currentDisplayPoint,
            IsShiftPressed(e));
        if (editedSourceBounds is RectD sourceBounds)
        {
            ShowDragRectanglePreview(ViewModel.CreateDisplayBoundsFromSourceBounds(sourceBounds));
        }
    }

    private async Task CompleteAnnotationDragAsync(
        Point currentDisplayPoint,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        RectD? editedSourceBounds = CreateAnnotationDragBounds(
            currentDisplayPoint,
            IsShiftPressed(e));
        Guid annotationId = _annotationDragId;
        string displayName = _annotationDragHandle == AnnotationBoundsHandle.Move
            ? "Move Annotation"
            : "Resize Annotation";

        EndCanvasDrag(e);

        if (editedSourceBounds is RectD sourceBounds)
        {
            await ViewModel.UpdateAnnotationBoundsAsync(
                annotationId,
                sourceBounds,
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

    private void EndCanvasDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _canvasDragMode = CanvasDragMode.None;
        _annotationDragId = Guid.Empty;
        _annotationDragHandle = AnnotationBoundsHandle.Move;
        _annotationDragOriginalSourceBounds = default;
        _annotationDragStartSourcePoint = default;
        CanvasSurface.ReleasePointerCapture(e.Pointer);
        DragRectanglePreview.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void ShowDragRectanglePreview(RectD displayBounds)
    {
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(DragRectanglePreview, displayBounds.X);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(DragRectanglePreview, displayBounds.Y);
        DragRectanglePreview.Width = Math.Max(1, displayBounds.Width);
        DragRectanglePreview.Height = Math.Max(1, displayBounds.Height);
        DragRectanglePreview.Visibility = Visibility.Visible;
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

        double deltaX = currentPoint.X - _rectangleDragStart.X;
        double deltaY = currentPoint.Y - _rectangleDragStart.Y;
        double side = Math.Min(Math.Abs(deltaX), Math.Abs(deltaY));
        return ClampToCanvas(new Point(
            _rectangleDragStart.X + Math.Sign(deltaX) * side,
            _rectangleDragStart.Y + Math.Sign(deltaY) * side));
    }

    private void UpdateBindingsFromViewModel()
    {
        _isUpdatingBindings = true;
        try
        {
            Bindings.Update();
        }
        finally
        {
            _isUpdatingBindings = false;
        }

        UpdateAnnotationToolbarSelection();
    }

    private void UpdateAnnotationToolbarSelection()
    {
        UpdateAnnotationToolButton(RectangleToolButton, 0);
        UpdateAnnotationToolButton(EllipseToolButton, 1);
        UpdateAnnotationToolButton(LineToolButton, 2);
        UpdateAnnotationToolButton(ArrowToolButton, 3);
        UpdateAnnotationToolButton(TextToolButton, 4);
        UpdateAnnotationToolButton(HighlightToolButton, 5);
        UpdateAnnotationToolButton(BlurToolButton, 6);
    }

    private void UpdateAnnotationToolButton(Button button, int toolIndex)
    {
        bool isActive = ViewModel.AnnotationToolIndex == toolIndex;
        button.Background = isActive
            ? (Brush)Resources["SnapAccentMutedBrush"]
            : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        button.BorderBrush = isActive
            ? (Brush)Resources["SnapAccentBrush"]
            : (Brush)Resources["SnapStrokeBrush"];
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

    private static bool IsAnnotationInteraction(object originalSource)
    {
        var element = originalSource as FrameworkElement;
        while (element is not null)
        {
            if (element.DataContext is AnnotationOverlayItem)
            {
                return true;
            }

            element = element.Parent as FrameworkElement;
        }

        return false;
    }
}
