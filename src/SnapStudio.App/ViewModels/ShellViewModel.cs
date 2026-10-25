using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using SnapStudio.App.ViewModels.Items;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Diagnostics;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;
using SnapStudio.Core.Messaging;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.Settings;
using SnapStudio.Core.System;
using SnapStudio.Ipc;
using SnapStudio.Rendering;

namespace SnapStudio.App.ViewModels;

public enum CaptureMode
{
    Region,
    Picker,
    Display,
    FullScreen,
    Window
}

public enum ScreenRecordingAudioMode
{
    None,
    Microphone,
    SystemAudio
}

public enum DocumentHistoryFilter
{
    All,
    Captures,
    Imports,
    Scrolling,
    Duplicates
}

public sealed partial class ShellViewModel : IEditorMessageHandler, INotifyPropertyChanged, IDisposable
{
    private bool _fillInProgress;
    private int _documentChangeVersion;
    private double _fillTolerance = 16;
    private const int ThumbnailMaximumPixelSize = 128;
    private const string PrivacyDocumentFileName = "privacy.md";
    private const string StillCaptureFeatureFlag = "Capture.WgcStill";
    private const string DelayedCaptureFeatureFlag = "Capture.Delayed";
    private const string IncludeCursorFeatureFlag = "Capture.IncludeCursor";
    private const string GdiFallbackFeatureFlag = "Capture.GdiFallback";
    private const string BlurToolFeatureFlag = "Editor.BlurTool";
    private const string PdfExportFeatureFlag = "Editor.PdfExport";
    private const string PinToScreenFeatureFlag = "Workspace.PinToScreen";
    private const string OcrFeatureFlag = "V1.Ocr";
    private const string ScrollingCaptureFeatureFlag = "V1.ScrollingCapture";
    private const string ScreenRecordingFeatureFlag = "V1.ScreenRecording";
    private const string SmartRedactFeatureFlag = "V2.SmartRedact";
    private const string PluginSdkFeatureFlag = "V2.PluginSdk";
    private const double ResizeMinimumDimension = 1;
    private const double ResizeMaximumDimension = 20000;
    private const double ResizeMinimumPercent = 1;
    private const double ResizeMaximumPercent = 1000;
    private const double AnnotationStrokeMaximum = 12;
    private const double AnnotationTextSizeMaximum = 72;
    private const double AnnotationCornerRadiusDefault = 8;
    private const int AnnotationStrokeCustomIndex = 5;
    private const int AnnotationCornerStyleEdgesIndex = 0;
    private const int AnnotationCornerStyleRoundedIndex = 1;
    private const int ResizeUnitPixelsIndex = 0;
    private const int ResizeUnitPercentIndex = 1;
    private const int StorageBackendFileSystemIndex = 0;
    private const int StorageBackendDatabaseIndex = 1;

    private static readonly string[] CaptureHotkeyOptions =
    [
        "PrintScreen",
        "Ctrl+Shift+S",
        "Ctrl+Alt+S"
    ];

    private readonly IDocumentCatalog? _documentCatalog;
    private readonly IDocumentRenderer? _documentRenderer;
    private readonly IDocumentRasterEditor? _documentRasterEditor;
    private readonly IDocumentThumbnailCache? _documentThumbnailCache;
    private readonly IDocumentWorkspaceBootstrapper? _documentWorkspaceBootstrapper;
    private readonly ICrashRecoveryJournal? _crashRecoveryJournal;
    private readonly IDiagnosticLog? _diagnosticLog;
    private readonly IEditCommandStack? _editCommandStack;
    private readonly DocumentAnnotationEditor? _annotationEditor;
    private readonly ICaptureWorkflow? _captureWorkflow;
    private readonly IImageImportService? _imageImportService;
    private readonly IImageCreationService? _imageCreationService;
    private readonly IClipboardService? _clipboardService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly IDocumentRepository? _documentRepository;
    private Task _pendingDocumentSave = Task.CompletedTask;
    private readonly NamedPipeEditorMessageServer? _editorMessageServer;
    private readonly IExportDestinationPicker? _exportDestinationPicker;
    private readonly IReadOnlyDictionary<ExportFormat, IExportProvider>? _exportProviders;
    private readonly IFileTrashService? _fileTrashService;
    private readonly IFeatureFlagService? _featureFlags;
    private readonly IHotkeyService? _hotkeyService;
    private readonly IOcrTextExtractionService? _ocrTextExtractionService;
    private readonly IPinnedImageService? _pinnedImageService;
    private readonly ISettingsImportExportService? _settingsImportExportService;
    private readonly ISettingsFilePicker? _settingsFilePicker;
    private readonly ICaptureTargetSelector? _captureTargetSelector;
    private readonly IScrollingCaptureService? _scrollingCaptureService;
    private readonly IScreenRecordingService? _screenRecordingService;
    private readonly IScrollTargetDetector? _scrollTargetDetector;
    private readonly ISettingsStore? _settingsStore;
    private readonly IStorageLocationPicker? _storageLocationPicker;
    private readonly IWorkspaceShellService? _workspaceShellService;
    private readonly string _recoverySessionId = string.Empty;
    private readonly AnnotationSelectionState _annotationSelection = new();
    private readonly AnnotationLayerWorkspace _layerWorkspace = new();
    private readonly List<DocumentSummary> _allRecentDocuments = [];
    private readonly DocumentThumbnailRefreshCoordinator _recentThumbnailRefresh = new();
    private readonly CancellationTokenSource _recentThumbnailRefreshCancellation = new();
    private CancellationTokenSource? _messageLoopCancellation;
    private Task? _messageLoopTask;
    private bool _disposed;
    private CanvasViewportState _canvasViewport = CanvasViewportState.Empty;
    private BitmapImage? _canvasImageSource;
    private InfoBarSeverity _captureNoticeSeverity = InfoBarSeverity.Informational;
    private bool _captureNoticeIsOpen;
    private string _captureNoticeMessage = string.Empty;
    private string _captureNoticeTitle = string.Empty;
    private bool _copyCapturesToClipboard;
    private int _captureDelayIndex;
    private int _captureHotkeyIndex;
    private bool _firstRunCopyCapturesToClipboard;
    private int _firstRunCaptureHotkeyIndex;
    private bool _firstRunIncludeCursor = true;
    private bool _firstRunSetupIsOpen;
    private int _firstRunStorageBackendIndex;
    private string _firstRunStorageRoot = string.Empty;
    private bool _settingsSurfaceIsFirstRun;
    private bool _settingsStillCaptureEnabled = true;
    private bool _settingsDelayedCaptureEnabled = true;
    private bool _settingsCursorCaptureEnabled = true;
    private bool _settingsGdiFallbackEnabled;
    private bool _settingsBlurToolEnabled = true;
    private bool _settingsPdfExportEnabled = true;
    private bool _settingsPinToScreenEnabled = true;
    private bool _settingsOcrEnabled;
    private bool _settingsScrollingCaptureEnabled;
    private bool _settingsScreenRecordingEnabled;
    private bool _settingsSmartRedactEnabled;
    private bool _settingsPluginSdkEnabled;
    private int _screenRecordingAudioModeIndex;
    private bool _canvasFitToViewport = true;
    private CaptureDocument? _currentDocument;
    private DocumentId? _currentDocumentId;
    private string _currentDocumentDetail = "No document selected";
    private string _currentDocumentFileSizeLabel = "-";
    private string _currentDocumentFileNameLabel = "-";
    private string _currentDocumentFooterLabel = "No capture selected.";
    private string _currentDocumentHeightLabel = "-";
    private string _currentDocumentLocationLabel = "-";
    private string _currentDocumentTitle = "Canvas";
    private string _currentDocumentWidthLabel = "-";
    private ScreenRecordingSession? _activeScreenRecordingSession;
    private bool _captureInProgress;
    private bool _screenRecordingOperationInProgress;
    private int _annotationPresetIndex;
    private int _annotationToolIndex;
    private int _annotationStrokeIndex;
    private double _annotationStrokeThickness = 2;
    private string _annotationSizeText = "2";
    private double _annotationCornerRadius = AnnotationCornerRadiusDefault;
    private double _annotationOpacity = 1;
    private ColorRgba _customAnnotationColor = new(76, 194, 255, 255);
    private bool _resizeAspectRatioIsLocked = true;
    private double _resizeWidth = 1;
    private double _resizeHeight = 1;
    private int _resizeUnitIndex;
    private string _annotationText = "Text";
    private int _historyFilterIndex;
    private string _historySearchText = string.Empty;
    private bool _includeCursor = true;
    private string _statusText = "Ready";
    private ApplicationStorageBackend _storageBackend = ApplicationStorageBackend.FileSystem;
    private string _storageRoot = string.Empty;
    private string _workspaceTitleText = string.Empty;

    public ShellViewModel(
        ISettingsStore settingsStore,
        ISettingsImportExportService settingsImportExportService,
        ISettingsFilePicker settingsFilePicker,
        IDocumentCatalog documentCatalog,
        IDocumentRepository documentRepository,
        IDocumentThumbnailCache documentThumbnailCache,
        IDocumentWorkspaceBootstrapper documentWorkspaceBootstrapper,
        IEditCommandStack editCommandStack,
        IDocumentRenderer documentRenderer,
        IDocumentRasterEditor documentRasterEditor,
        ICaptureTargetSelector captureTargetSelector,
        ICaptureWorkflow captureWorkflow,
        IImageImportService imageImportService,
        IImageCreationService imageCreationService,
        IClipboardService clipboardService,
        IExportDestinationPicker exportDestinationPicker,
        IReadOnlyDictionary<ExportFormat, IExportProvider> exportProviders,
        IHotkeyService hotkeyService,
        IWorkspaceShellService workspaceShellService,
        IFileTrashService fileTrashService,
        IPinnedImageService pinnedImageService,
        IStorageLocationPicker storageLocationPicker,
        IDiagnosticLog diagnosticLog,
        ICrashRecoveryJournal crashRecoveryJournal,
        string recoverySessionId,
        IFeatureFlagService featureFlags,
        IOcrTextExtractionService ocrTextExtractionService,
        IScrollTargetDetector scrollTargetDetector,
        IScrollingCaptureService scrollingCaptureService,
        IScreenRecordingService screenRecordingService,
        NamedPipeEditorMessageServer editorMessageServer,
        DispatcherQueue dispatcherQueue,
        IHistoryFolderStore? historyFolderStore = null,
        IToolDesignStore? toolDesignStore = null,
        IToolDesignFilePicker? toolDesignFilePicker = null,
        IStampStore? stampStore = null,
        IOnlineStampCatalog? onlineStampCatalog = null)
    {
        _settingsStore = settingsStore;
        _settingsImportExportService = settingsImportExportService;
        _settingsFilePicker = settingsFilePicker;
        _documentCatalog = documentCatalog;
        _documentRepository = documentRepository;
        _documentThumbnailCache = documentThumbnailCache;
        _documentWorkspaceBootstrapper = documentWorkspaceBootstrapper;
        _editCommandStack = editCommandStack;
        _annotationEditor = new DocumentAnnotationEditor(editCommandStack);
        _documentRenderer = documentRenderer;
        _documentRasterEditor = documentRasterEditor;
        _captureTargetSelector = captureTargetSelector;
        _captureWorkflow = captureWorkflow;
        _imageImportService = imageImportService;
        _imageCreationService = imageCreationService;
        _clipboardService = clipboardService;
        _exportDestinationPicker = exportDestinationPicker;
        _exportProviders = exportProviders;
        _hotkeyService = hotkeyService;
        _workspaceShellService = workspaceShellService;
        _fileTrashService = fileTrashService;
        _pinnedImageService = pinnedImageService;
        _storageLocationPicker = storageLocationPicker;
        _diagnosticLog = diagnosticLog;
        _crashRecoveryJournal = crashRecoveryJournal;
        _recoverySessionId = string.IsNullOrWhiteSpace(recoverySessionId)
            ? Guid.NewGuid().ToString("N")
            : recoverySessionId;
        _featureFlags = featureFlags;
        _ocrTextExtractionService = ocrTextExtractionService;
        _scrollTargetDetector = scrollTargetDetector;
        _scrollingCaptureService = scrollingCaptureService;
        _screenRecordingService = screenRecordingService;
        _editorMessageServer = editorMessageServer;
        _dispatcherQueue = dispatcherQueue;
        _historyFolderStore = historyFolderStore;
        _toolDesignStore = toolDesignStore;
        _toolDesignFilePicker = toolDesignFilePicker;
        _stampStore = stampStore;
        _onlineStampCatalog = onlineStampCatalog;
        _hotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;
    }

    private ShellViewModel()
    {
    }

    public static ShellViewModel Empty { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DocumentSummaryItem> RecentDocuments { get; } = [];

    public ObservableCollection<AnnotationOverlayItem> AnnotationOverlays => _layerWorkspace.Overlays;

    public ObservableCollection<AnnotationLayerItem> AnnotationLayers => _layerWorkspace.Layers;

    public string AnnotationLayerCount => AnnotationLayers.Count.ToString(CultureInfo.InvariantCulture);

    public bool CanDeleteActiveLayer => _currentDocument is { Layers.Count: > 1 }
        && _layerWorkspace.ActiveLayerId is Guid activeLayerId
        && _currentDocument.Layers.Any(layer => layer.Id == activeLayerId);

    public Visibility AnnotationLayersEmptyVisibility => AnnotationLayers.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string AnnotationLayersEmptyMessage => HasCurrentDocument
        ? "Add a layer to organize objects."
        : "Open or capture an image to see layers.";

    public BitmapImage? CanvasImageSource
    {
        get => _canvasImageSource;
        private set
        {
            if (_canvasImageSource == value)
            {
                return;
            }

            _canvasImageSource = value;
            OnPropertyChanged(nameof(CanvasImageSource));
            OnPropertyChanged(nameof(CanvasImageVisibility));
            OnPropertyChanged(nameof(CanvasPlaceholderVisibility));
        }
    }

    public Visibility CanvasImageVisibility => CanvasImageSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility CanvasPlaceholderVisibility => CanvasImageSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double CanvasDisplayWidth => _canvasViewport.DisplayWidth;

    public double CanvasDisplayHeight => _canvasViewport.DisplayHeight;

    public string CanvasZoomLabel => _canvasViewport.HasSource
        ? $"{_canvasViewport.Zoom * 100:0}%"
        : "0%";

    public string CurrentDocumentSizeLabel => _currentDocument is null
        ? "-"
        : string.Format(
            CultureInfo.InvariantCulture,
            "{0} x {1} px",
            _currentDocument.SourceImage.Width,
            _currentDocument.SourceImage.Height);

    public string CurrentDocumentFooterLabel
    {
        get => _currentDocumentFooterLabel;
        private set
        {
            if (_currentDocumentFooterLabel == value)
            {
                return;
            }

            _currentDocumentFooterLabel = value;
            OnPropertyChanged(nameof(CurrentDocumentFooterLabel));
        }
    }

    public bool CanFitCanvas => _canvasViewport.HasSource;

    public bool CanZoomIn => _canvasViewport.HasSource
        && _canvasViewport.Zoom < CanvasViewportState.MaximumZoom;

    public bool CanZoomOut => _canvasViewport.HasSource
        && _canvasViewport.Zoom > CanvasViewportState.MinimumZoom;

    public bool CanUndo => _editCommandStack?.CanUndo == true;

    public bool CanRedo => _editCommandStack?.CanRedo == true;

    public string UndoToolTip => _editCommandStack?.UndoDisplayName is { Length: > 0 } displayName
        ? $"Undo {displayName}"
        : "Undo";

    public string RedoToolTip => _editCommandStack?.RedoDisplayName is { Length: > 0 } displayName
        ? $"Redo {displayName}"
        : "Redo";

    public bool HasSelectedAnnotation => _annotationSelection.HasSelection;

    public Guid? SelectedAnnotationId => _annotationSelection.SelectedAnnotationId;

    public DocumentId? CurrentDocumentId => _currentDocument?.Id;

    public bool CanDeleteSelectedAnnotation => HasSelectedAnnotation && HasCurrentDocument;

    public bool CanCropToSelection => HasCurrentDocument && SourceSelectionBounds is { Width: >= 1, Height: >= 1 };

    public bool OcrIsEnabled => _featureFlags?.IsEnabled(OcrFeatureFlag) == true;

    public Visibility OcrControlsVisibility => OcrIsEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool ScrollingCaptureIsEnabled => _featureFlags?.IsEnabled(ScrollingCaptureFeatureFlag) == true;

    public Visibility ScrollingCaptureControlsVisibility => ScrollingCaptureIsEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool ScreenRecordingIsEnabled => _featureFlags?.IsEnabled(ScreenRecordingFeatureFlag) == true;

    public Visibility ScreenRecordingControlsVisibility => ScreenRecordingIsEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanCopyRecognizedText => OcrIsEnabled
        && HasCurrentDocument
        && _ocrTextExtractionService is not null
        && _clipboardService is not null;

    public bool CanCaptureScrolling => ScrollingCaptureIsEnabled
        && !_captureInProgress
        && _scrollTargetDetector is not null
        && _scrollingCaptureService is not null
        && _documentRepository is not null;

    public bool IsScreenRecording => _activeScreenRecordingSession is not null;

    public string ScreenRecordingCommandText => IsScreenRecording
        ? "Stop Recording"
        : "Record Screen";

    public string ScreenRecordingAutomationName => IsScreenRecording
        ? "Stop screen recording"
        : "Start screen recording";

    public Visibility ScreenRecordingIndicatorVisibility => IsScreenRecording
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ScreenRecordingIndicatorText => IsScreenRecording
        ? "Recording"
        : string.Empty;

    public bool CanToggleScreenRecording => ScreenRecordingIsEnabled
        && !_screenRecordingOperationInProgress
        && (IsScreenRecording || !_captureInProgress)
        && _captureTargetSelector is not null
        && _screenRecordingService is not null;

    public bool CanChangeScreenRecordingAudioMode => ScreenRecordingIsEnabled
        && !IsScreenRecording
        && !_screenRecordingOperationInProgress;

    public string CurrentDocumentTitle
    {
        get => _currentDocumentTitle;
        private set
        {
            if (_currentDocumentTitle == value)
            {
                return;
            }

            _currentDocumentTitle = value;
            OnPropertyChanged(nameof(CurrentDocumentTitle));
        }
    }

    public string CurrentDocumentDetail
    {
        get => _currentDocumentDetail;
        private set
        {
            if (_currentDocumentDetail == value)
            {
                return;
            }

            _currentDocumentDetail = value;
            OnPropertyChanged(nameof(CurrentDocumentDetail));
        }
    }

    public string CurrentDocumentWidthLabel
    {
        get => _currentDocumentWidthLabel;
        private set
        {
            if (_currentDocumentWidthLabel == value)
            {
                return;
            }

            _currentDocumentWidthLabel = value;
            OnPropertyChanged(nameof(CurrentDocumentWidthLabel));
        }
    }

    public string CurrentDocumentHeightLabel
    {
        get => _currentDocumentHeightLabel;
        private set
        {
            if (_currentDocumentHeightLabel == value)
            {
                return;
            }

            _currentDocumentHeightLabel = value;
            OnPropertyChanged(nameof(CurrentDocumentHeightLabel));
        }
    }

    public string CurrentDocumentFileSizeLabel
    {
        get => _currentDocumentFileSizeLabel;
        private set
        {
            if (_currentDocumentFileSizeLabel == value)
            {
                return;
            }

            _currentDocumentFileSizeLabel = value;
            OnPropertyChanged(nameof(CurrentDocumentFileSizeLabel));
        }
    }

    public string CurrentDocumentFileNameLabel
    {
        get => _currentDocumentFileNameLabel;
        private set
        {
            if (_currentDocumentFileNameLabel == value)
            {
                return;
            }

            _currentDocumentFileNameLabel = value;
            OnPropertyChanged(nameof(CurrentDocumentFileNameLabel));
        }
    }

    public string CurrentDocumentLocationLabel
    {
        get => _currentDocumentLocationLabel;
        private set
        {
            if (_currentDocumentLocationLabel == value)
            {
                return;
            }

            _currentDocumentLocationLabel = value;
            OnPropertyChanged(nameof(CurrentDocumentLocationLabel));
        }
    }

    public bool IncludeCursor
    {
        get => _includeCursor;
        set
        {
            if (_includeCursor == value)
            {
                return;
            }

            _includeCursor = value;
            OnPropertyChanged(nameof(IncludeCursor));
        }
    }

    public bool CopyCapturesToClipboard
    {
        get => _copyCapturesToClipboard;
        private set
        {
            if (_copyCapturesToClipboard == value)
            {
                return;
            }

            _copyCapturesToClipboard = value;
            OnPropertyChanged(nameof(CopyCapturesToClipboard));
        }
    }

    public int CaptureDelayIndex
    {
        get => _captureDelayIndex;
        set
        {
            if (_captureDelayIndex == value)
            {
                return;
            }

            _captureDelayIndex = value;
            OnPropertyChanged(nameof(CaptureDelayIndex));
            OnPropertyChanged(nameof(CaptureDelay));
        }
    }

    public TimeSpan CaptureDelay => CaptureDelayIndex switch
    {
        1 => TimeSpan.FromSeconds(3),
        2 => TimeSpan.FromSeconds(5),
        _ => TimeSpan.Zero
    };

    public int CaptureHotkeyIndex
    {
        get => _captureHotkeyIndex;
        set
        {
            if (_captureHotkeyIndex == value)
            {
                return;
            }

            _captureHotkeyIndex = value;
            OnPropertyChanged(nameof(CaptureHotkeyIndex));
        }
    }

    public int ScreenRecordingAudioModeIndex
    {
        get => _screenRecordingAudioModeIndex;
        private set
        {
            int normalized = Math.Clamp(value, 0, 2);
            if (_screenRecordingAudioModeIndex == normalized)
            {
                return;
            }

            _screenRecordingAudioModeIndex = normalized;
            OnPropertyChanged(nameof(ScreenRecordingAudioModeIndex));
            OnPropertyChanged(nameof(ScreenRecordingAudioMode));
        }
    }

    public ScreenRecordingAudioMode ScreenRecordingAudioMode => ScreenRecordingAudioModeIndex switch
    {
        1 => ScreenRecordingAudioMode.Microphone,
        2 => ScreenRecordingAudioMode.SystemAudio,
        _ => ScreenRecordingAudioMode.None
    };

    public bool FirstRunSetupIsOpen
    {
        get => _firstRunSetupIsOpen;
        private set
        {
            if (_firstRunSetupIsOpen == value)
            {
                return;
            }

            _firstRunSetupIsOpen = value;
            OnPropertyChanged(nameof(FirstRunSetupIsOpen));
            OnPropertyChanged(nameof(FirstRunSetupVisibility));
        }
    }

    public Visibility FirstRunSetupVisibility => FirstRunSetupIsOpen
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool SettingsSurfaceIsFirstRun
    {
        get => _settingsSurfaceIsFirstRun;
        private set
        {
            if (_settingsSurfaceIsFirstRun == value)
            {
                return;
            }

            _settingsSurfaceIsFirstRun = value;
            OnPropertyChanged(nameof(SettingsSurfaceIsFirstRun));
            OnPropertyChanged(nameof(SettingsPanelTitle));
            OnPropertyChanged(nameof(SettingsPrimaryActionText));
            OnPropertyChanged(nameof(SettingsPrimaryActionAutomationName));
            OnPropertyChanged(nameof(SettingsPrimaryActionHelpText));
            OnPropertyChanged(nameof(SettingsCloseButtonVisibility));
        }
    }

    public string SettingsPanelTitle => SettingsSurfaceIsFirstRun
        ? "SnapStudio setup"
        : "Settings";

    public string SettingsPrimaryActionText => SettingsSurfaceIsFirstRun
        ? "Finish setup"
        : "Save";

    public string SettingsPrimaryActionAutomationName => SettingsSurfaceIsFirstRun
        ? "Finish setup"
        : "Save settings";

    public string SettingsPrimaryActionHelpText => SettingsSurfaceIsFirstRun
        ? "Save setup choices and open the workspace."
        : "Save settings choices and close settings.";

    public Visibility SettingsCloseButtonVisibility => SettingsSurfaceIsFirstRun
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string FirstRunStorageRoot
    {
        get => _firstRunStorageRoot;
        private set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_firstRunStorageRoot, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _firstRunStorageRoot = normalized;
            OnPropertyChanged(nameof(FirstRunStorageRoot));
        }
    }

    public int FirstRunCaptureHotkeyIndex
    {
        get => _firstRunCaptureHotkeyIndex;
        private set
        {
            if (_firstRunCaptureHotkeyIndex == value)
            {
                return;
            }

            _firstRunCaptureHotkeyIndex = value;
            OnPropertyChanged(nameof(FirstRunCaptureHotkeyIndex));
        }
    }

    public int FirstRunStorageBackendIndex
    {
        get => _firstRunStorageBackendIndex;
        private set
        {
            if (_firstRunStorageBackendIndex == value)
            {
                return;
            }

            _firstRunStorageBackendIndex = value;
            OnPropertyChanged(nameof(FirstRunStorageBackendIndex));
        }
    }

    public bool FirstRunIncludeCursor
    {
        get => _firstRunIncludeCursor;
        set
        {
            if (_firstRunIncludeCursor == value)
            {
                return;
            }

            _firstRunIncludeCursor = value;
            OnPropertyChanged(nameof(FirstRunIncludeCursor));
        }
    }

    public bool FirstRunCopyCapturesToClipboard
    {
        get => _firstRunCopyCapturesToClipboard;
        set
        {
            if (_firstRunCopyCapturesToClipboard == value)
            {
                return;
            }

            _firstRunCopyCapturesToClipboard = value;
            OnPropertyChanged(nameof(FirstRunCopyCapturesToClipboard));
        }
    }

    public bool CanChooseFirstRunStorageRoot => _storageLocationPicker is not null;

    public bool CanImportSettings => _settingsStore is not null
        && _settingsImportExportService is not null
        && _settingsFilePicker is not null;

    public bool CanExportSettings => _settingsStore is not null
        && _settingsImportExportService is not null
        && _settingsFilePicker is not null;

    public bool CanOpenPrivacyNotice => _workspaceShellService is not null;

    public bool SettingsStillCaptureEnabled
    {
        get => _settingsStillCaptureEnabled;
        set
        {
            if (_settingsStillCaptureEnabled == value)
            {
                return;
            }

            _settingsStillCaptureEnabled = value;
            OnPropertyChanged(nameof(SettingsStillCaptureEnabled));
        }
    }

    public bool SettingsDelayedCaptureEnabled
    {
        get => _settingsDelayedCaptureEnabled;
        set
        {
            if (_settingsDelayedCaptureEnabled == value)
            {
                return;
            }

            _settingsDelayedCaptureEnabled = value;
            OnPropertyChanged(nameof(SettingsDelayedCaptureEnabled));
        }
    }

    public bool SettingsCursorCaptureEnabled
    {
        get => _settingsCursorCaptureEnabled;
        set
        {
            if (_settingsCursorCaptureEnabled == value)
            {
                return;
            }

            _settingsCursorCaptureEnabled = value;
            OnPropertyChanged(nameof(SettingsCursorCaptureEnabled));
        }
    }

    public bool SettingsGdiFallbackEnabled
    {
        get => _settingsGdiFallbackEnabled;
        set
        {
            if (_settingsGdiFallbackEnabled == value)
            {
                return;
            }

            _settingsGdiFallbackEnabled = value;
            OnPropertyChanged(nameof(SettingsGdiFallbackEnabled));
        }
    }

    public bool SettingsBlurToolEnabled
    {
        get => _settingsBlurToolEnabled;
        set
        {
            if (_settingsBlurToolEnabled == value)
            {
                return;
            }

            _settingsBlurToolEnabled = value;
            OnPropertyChanged(nameof(SettingsBlurToolEnabled));
        }
    }

    public bool SettingsPdfExportEnabled
    {
        get => _settingsPdfExportEnabled;
        set
        {
            if (_settingsPdfExportEnabled == value)
            {
                return;
            }

            _settingsPdfExportEnabled = value;
            OnPropertyChanged(nameof(SettingsPdfExportEnabled));
        }
    }

    public bool SettingsPinToScreenEnabled
    {
        get => _settingsPinToScreenEnabled;
        set
        {
            if (_settingsPinToScreenEnabled == value)
            {
                return;
            }

            _settingsPinToScreenEnabled = value;
            OnPropertyChanged(nameof(SettingsPinToScreenEnabled));
        }
    }

    public bool SettingsOcrEnabled
    {
        get => _settingsOcrEnabled;
        set
        {
            if (_settingsOcrEnabled == value)
            {
                return;
            }

            _settingsOcrEnabled = value;
            OnPropertyChanged(nameof(SettingsOcrEnabled));
        }
    }

    public bool SettingsScrollingCaptureEnabled
    {
        get => _settingsScrollingCaptureEnabled;
        set
        {
            if (_settingsScrollingCaptureEnabled == value)
            {
                return;
            }

            _settingsScrollingCaptureEnabled = value;
            OnPropertyChanged(nameof(SettingsScrollingCaptureEnabled));
        }
    }

    public bool SettingsScreenRecordingEnabled
    {
        get => _settingsScreenRecordingEnabled;
        set
        {
            if (_settingsScreenRecordingEnabled == value)
            {
                return;
            }

            _settingsScreenRecordingEnabled = value;
            OnPropertyChanged(nameof(SettingsScreenRecordingEnabled));
        }
    }

    public bool SettingsSmartRedactEnabled
    {
        get => _settingsSmartRedactEnabled;
        set
        {
            if (_settingsSmartRedactEnabled == value)
            {
                return;
            }

            _settingsSmartRedactEnabled = value;
            OnPropertyChanged(nameof(SettingsSmartRedactEnabled));
        }
    }

    public bool SettingsPluginSdkEnabled
    {
        get => _settingsPluginSdkEnabled;
        set
        {
            if (_settingsPluginSdkEnabled == value)
            {
                return;
            }

            _settingsPluginSdkEnabled = value;
            OnPropertyChanged(nameof(SettingsPluginSdkEnabled));
        }
    }

    public bool HasCurrentDocument => _currentDocumentId.HasValue;

    public bool CanRenameCurrentDocument => HasCurrentDocument
        && !string.IsNullOrWhiteSpace(WorkspaceTitleText);

    public bool CanDuplicateCurrentDocument => HasCurrentDocument;

    public bool CanDeleteCurrentDocument => HasCurrentDocument && CanManageCaptureHistory;

    public bool CanMoveCurrentCaptureToTrash => HasCurrentDocument && CanManageCaptureHistory
        && _fileTrashService is not null
        && _currentDocument?.SourceImage.Path is { Length: > 0 } path
        && File.Exists(path);

    public bool CanOpenCurrentDocumentFolder => HasCurrentDocument;

    public bool CanPinCurrentDocument => HasCurrentDocument;

    public bool CanOpenImageFile => _imageImportService is not null
        && _documentRepository is not null
        && !_captureInProgress
        && !_screenRecordingOperationInProgress
        && !IsScreenRecording;

    public bool CanCreateImage => _imageCreationService is not null
        && _documentRepository is not null
        && !_captureInProgress
        && !_screenRecordingOperationInProgress
        && !IsScreenRecording;

    public bool CanPasteImageAsLayer => CanCreateImage && HasCurrentDocument && _annotationEditor is not null;

    public string WorkspaceTitleText
    {
        get => _workspaceTitleText;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_workspaceTitleText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _workspaceTitleText = normalized;
            OnPropertyChanged(nameof(WorkspaceTitleText));
            OnPropertyChanged(nameof(CanRenameCurrentDocument));
        }
    }

    public int AnnotationPresetIndex
    {
        get => _annotationPresetIndex;
        private set
        {
            int normalized = Math.Clamp(value, 0, 6);
            if (_annotationPresetIndex == normalized)
            {
                return;
            }

            _annotationPresetIndex = normalized;
            OnPropertyChanged(nameof(AnnotationPresetIndex));
        }
    }

    public bool IsSelectionToolActive { get; private set; } = true;
    public bool IsMarqueeToolActive => IsSelectionToolActive && _isMarqueeTool;
    public bool IsMoveToolActive => IsSelectionToolActive && !_isMarqueeTool;
    private bool _isMarqueeTool = true;

    public bool CanAddActiveAnnotation => HasCurrentDocument && !IsSelectionToolActive && !IsStampToolActive
        && ActiveAnnotationKind is not (AnnotationKind.Pen or AnnotationKind.Fill);

    public Visibility FillToleranceControlsVisibility => !HasSelectedAnnotation && !IsSelectionToolActive && ActiveAnnotationKind == AnnotationKind.Fill
        ? Visibility.Visible : Visibility.Collapsed;

    public double FillTolerance
    {
        get => _fillTolerance;
        set
        {
            _fillTolerance = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 0, 255) : 16;
            OnPropertyChanged(nameof(FillTolerance));
        }
    }

    public AnnotationStyle DrawingStyle => CreateAnnotationStyle(ActiveAnnotationKind);

    public double CanvasZoom => _canvasViewport.Zoom;

    public void UseSelectionTool() => SetSelectionTool(marquee: true);

    public void UseMoveTool() => SetSelectionTool(marquee: false);

    private void SetSelectionTool(bool marquee)
    {
        _activeStamp = null;
        _pixelSelectionRequestVersion++;
        _isMarqueeTool = marquee;
        IsSelectionToolActive = true;
        OnPropertyChanged(nameof(IsSelectionToolActive));
        OnPropertyChanged(nameof(IsMarqueeToolActive));
        OnPropertyChanged(nameof(IsMoveToolActive));
        OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
        OnPropertyChanged(nameof(CanAddActiveAnnotation));
        OnPropertyChanged(nameof(FillToleranceControlsVisibility));
        StatusText = marquee ? $"{AreaSelectionToolName}: {AreaSelectionHint}. Copy, fill, or crop the selection."
            : "Move: click an object to select it, then drag to move. Shift-click adds or removes objects. M selects an area.";
        RefreshAnnotationOverlays();
        NotifyAnnotationStyleControlsChanged();
        NotifyPropertyContextChanged();
    }

    public int AnnotationToolIndex
    {
        get => _annotationToolIndex;
        set
        {
            if (_annotationToolIndex == value)
            {
                return;
            }

            _annotationToolIndex = Math.Clamp(value, 0, 9);
            OnPropertyChanged(nameof(AnnotationToolIndex));
            OnPropertyChanged(nameof(ActiveAnnotationKind));
            OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
            OnPropertyChanged(nameof(CanAddActiveAnnotation));
            NotifyAnnotationStyleControlsChanged();
        }
    }

    public AnnotationKind ActiveAnnotationKind => ActiveSvgToolDesign is not null ? AnnotationKind.Vector : ActiveToolFamily;

    public AnnotationKind ActiveToolFamily => AnnotationToolIndex switch
    {
        1 => AnnotationKind.Ellipse,
        2 => AnnotationKind.Line,
        3 => AnnotationKind.Arrow,
        4 => AnnotationKind.Text,
        5 => AnnotationKind.Highlight,
        6 => AnnotationKind.Blur,
        7 => AnnotationKind.Pen,
        8 => AnnotationKind.Fill,
        9 => AnnotationKind.Vector,
        _ => AnnotationKind.Rectangle
    };

    public string ActiveAnnotationToolLabel => IsStampToolActive ? $"Stamp · {ActiveStampName}" : IsSelectionToolActive ? (IsMarqueeToolActive ? AreaSelectionToolName : "Move") : ActiveSvgToolDesign?.Name ?? ActiveAnnotationKind switch
    {
        AnnotationKind.Ellipse => "Ellipse",
        AnnotationKind.Line => "Line",
        AnnotationKind.Arrow => "Arrow",
        AnnotationKind.Text => "Text",
        AnnotationKind.Highlight => "Highlighter",
        AnnotationKind.Blur => "Blur",
        AnnotationKind.Pen => "Pen",
        AnnotationKind.Fill => "Fill bucket",
        AnnotationKind.Vector => "Custom shape",
        _ => "Rectangle"
    };

    private AnnotationKind ActiveOrSelectedAnnotationKind => (SelectedAnnotationCount > 1
        ? _currentDocument?.Annotations.FirstOrDefault(item => SelectedAnnotationIds.Contains(item.Id) && item.Kind != AnnotationKind.Image)?.Kind
        : null) ?? GetSelectedAnnotation()?.Kind ?? ActiveAnnotationKind;

    public int AnnotationStrokeIndex
    {
        get => _annotationStrokeIndex;
        set
        {
            if (_annotationStrokeIndex == value)
            {
                return;
            }

            _annotationStrokeIndex = value;
            OnPropertyChanged(nameof(AnnotationStrokeIndex));
        }
    }

    public double AnnotationStrokeThickness
    {
        get => _annotationStrokeThickness;
        set => SetAnnotationStrokeThickness(value, preserveInputText: false);
    }

    public string AnnotationSizeText => _annotationSizeText;

    private void SetAnnotationStrokeThickness(double value, bool preserveInputText)
    {
        if (!double.IsFinite(value)) return;
        double normalized = Math.Clamp(value, 1, AnnotationSizeInputMaximum);
        bool changed = Math.Abs(_annotationStrokeThickness - normalized) >= 0.01;
        _annotationStrokeThickness = normalized;
        if (!preserveInputText || normalized != value) ResetAnnotationSizeText();
        if (!changed) return;
        OnPropertyChanged(nameof(AnnotationStrokeThickness));
        OnPropertyChanged(nameof(AnnotationSizeSliderValue));
    }

    private void SetAnnotationSizeText(string text)
    {
        if (_annotationSizeText == text) return;
        _annotationSizeText = text;
        OnPropertyChanged(nameof(AnnotationSizeText));
    }

    public void ResetAnnotationSizeText() => SetAnnotationSizeText(AnnotationStrokeThickness.ToString(CultureInfo.CurrentCulture));

    public string AnnotationSizeHeader => ActiveOrSelectedAnnotationKind == AnnotationKind.Text
        ? "Text size"
        : "Stroke width";

    public double AnnotationSizeMaximum => ActiveOrSelectedAnnotationKind == AnnotationKind.Text
        ? AnnotationTextSizeMaximum
        : AnnotationStrokeMaximum;

    public double AnnotationSizeInputMaximum => double.MaxValue;

    public double AnnotationSizeSliderValue => Math.Clamp(AnnotationStrokeThickness, 1, AnnotationSizeMaximum);

    public double AnnotationSizeStepFrequency => ActiveOrSelectedAnnotationKind == AnnotationKind.Text
        ? 1
        : 0.5;

    public int AnnotationCornerStyleIndex => AnnotationCornerRadius > 0
        ? AnnotationCornerStyleRoundedIndex
        : AnnotationCornerStyleEdgesIndex;

    public double AnnotationCornerRadius
    {
        get => _annotationCornerRadius;
        set
        {
            double normalized = double.IsFinite(value)
                ? Math.Max(0, Math.Round(value))
                : 0;
            if (Math.Abs(_annotationCornerRadius - normalized) < 0.01)
            {
                return;
            }

            _annotationCornerRadius = normalized;
            OnPropertyChanged(nameof(AnnotationCornerRadius));
            OnPropertyChanged(nameof(AnnotationCornerStyleIndex));
        }
    }

    public double AnnotationOpacity
    {
        get => _annotationOpacity;
        set
        {
            double normalized = Math.Clamp(value, 0.1, 1);
            if (Math.Abs(_annotationOpacity - normalized) < 0.01)
            {
                return;
            }

            _annotationOpacity = normalized;
            OnPropertyChanged(nameof(AnnotationOpacity));
        }
    }

    public string AnnotationText
    {
        get => _annotationText;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? "Text"
                : value.Trim();
            if (string.Equals(_annotationText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _annotationText = normalized;
            OnPropertyChanged(nameof(AnnotationText));
        }
    }

    public int HistoryFilterIndex
    {
        get => _historyFilterIndex;
        private set
        {
            int normalized = Math.Clamp(value, 0, 4);
            if (_historyFilterIndex == normalized)
            {
                return;
            }

            _historyFilterIndex = normalized;
            OnPropertyChanged(nameof(HistoryFilterIndex));
            OnPropertyChanged(nameof(HistoryFilter));
        }
    }

    public DocumentHistoryFilter HistoryFilter => HistoryFilterIndex switch
    {
        1 => DocumentHistoryFilter.Captures,
        2 => DocumentHistoryFilter.Imports,
        3 => DocumentHistoryFilter.Scrolling,
        4 => DocumentHistoryFilter.Duplicates,
        _ => DocumentHistoryFilter.All
    };

    public double ResizeWidth
    {
        get => _resizeWidth;
        set
        {
            double normalized = NormalizeResizeValue(value);
            if (ResizeAspectRatioIsLocked)
            {
                if (ResizeUsesPercent)
                {
                    SetResizeValues(normalized, normalized);
                    return;
                }

                if (TryGetResizeAspectRatio(out double aspectRatio))
                {
                    ResizeDimensions dimensions = CreateResizeDimensionsFromWidth(normalized, aspectRatio);
                    SetResizeValues(dimensions.Width, dimensions.Height);
                    return;
                }
            }

            SetResizeWidth(normalized);
        }
    }

    public double ResizeHeight
    {
        get => _resizeHeight;
        set
        {
            double normalized = NormalizeResizeValue(value);
            if (ResizeAspectRatioIsLocked)
            {
                if (ResizeUsesPercent)
                {
                    SetResizeValues(normalized, normalized);
                    return;
                }

                if (TryGetResizeAspectRatio(out double aspectRatio))
                {
                    ResizeDimensions dimensions = CreateResizeDimensionsFromHeight(normalized, aspectRatio);
                    SetResizeValues(dimensions.Width, dimensions.Height);
                    return;
                }
            }

            SetResizeHeight(normalized);
        }
    }

    public bool ResizeAspectRatioIsLocked
    {
        get => _resizeAspectRatioIsLocked;
        set
        {
            if (_resizeAspectRatioIsLocked == value)
            {
                return;
            }

            _resizeAspectRatioIsLocked = value;
            OnPropertyChanged(nameof(ResizeAspectRatioIsLocked));

            if (value && TryGetResizeAspectRatio(out double aspectRatio))
            {
                if (ResizeUsesPercent)
                {
                    SetResizeValues(_resizeWidth, _resizeWidth);
                    return;
                }

                ResizeDimensions dimensions = CreateResizeDimensionsFromWidth(_resizeWidth, aspectRatio);
                SetResizeValues(dimensions.Width, dimensions.Height);
            }
        }
    }

    public int ResizeUnitIndex
    {
        get => _resizeUnitIndex;
        set
        {
            int normalized = Math.Clamp(value, ResizeUnitPixelsIndex, ResizeUnitPercentIndex);
            if (_resizeUnitIndex == normalized)
            {
                return;
            }

            ResizeDimensions targetDimensions = CreateTargetResizeDimensions();
            _resizeUnitIndex = normalized;
            OnPropertyChanged(nameof(ResizeUnitIndex));
            NotifyResizeUnitChanged();
            SetResizeValuesFromTargetDimensions(targetDimensions);
        }
    }

    public string ResizeWidthHeader => ResizeUsesPercent ? "Width (%)" : "Width (px)";

    public string ResizeHeightHeader => ResizeUsesPercent ? "Height (%)" : "Height (px)";

    public double ResizeMaximumValue => ResizeUsesPercent
        ? GetResizeMaximumPercent()
        : ResizeMaximumDimension;

    public double ResizeStepFrequency => ResizeUsesPercent ? 5 : 1;

    private bool ResizeUsesPercent => ResizeUnitIndex == ResizeUnitPercentIndex;

    public async Task UpdateAnnotationStrokeAsync(
        int strokeIndex,
        CancellationToken cancellationToken)
    {
        if (strokeIndex < 0 || strokeIndex > AnnotationStrokeCustomIndex)
        {
            return;
        }

        MarkAnnotationPresetCustom();
        AnnotationStrokeIndex = strokeIndex;
        await ApplySelectedAnnotationStyleAsync("Style Annotation", cancellationToken, SelectionStyleAspect.Color)
            .ConfigureAwait(true);
    }

    public async Task UpdateAnnotationCustomColorAsync(
        byte red,
        byte green,
        byte blue,
        byte alpha,
        CancellationToken cancellationToken)
    {
        MarkAnnotationPresetCustom();
        _customAnnotationColor = new ColorRgba(red, green, blue, alpha);
        AnnotationStrokeIndex = AnnotationStrokeCustomIndex;
        await ApplySelectedAnnotationStyleAsync("Style Annotation", cancellationToken, SelectionStyleAspect.Color)
            .ConfigureAwait(true);
    }

    public async Task UpdateAnnotationStrokeThicknessAsync(
        double strokeThickness,
        CancellationToken cancellationToken)
    {
        MarkAnnotationPresetCustom();
        AnnotationStrokeThickness = strokeThickness;
        await ApplySelectedAnnotationStyleAsync("Style Annotation", cancellationToken, SelectionStyleAspect.Width)
            .ConfigureAwait(true);
    }

    public async Task UpdateAnnotationSizeTextAsync(string text, CancellationToken cancellationToken)
    {
        // Keep incomplete input (such as a trailing decimal point) through binding refreshes.
        SetAnnotationSizeText(text);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || !double.IsFinite(value) || value < 1) return;
        MarkAnnotationPresetCustom();
        SetAnnotationStrokeThickness(value, preserveInputText: true);
        await ApplySelectedAnnotationStyleAsync("Style Annotation", cancellationToken, SelectionStyleAspect.Width).ConfigureAwait(true);
    }

    public async Task UpdateAnnotationCornerStyleAsync(
        int cornerStyleIndex,
        CancellationToken cancellationToken)
    {
        if (cornerStyleIndex is not (AnnotationCornerStyleEdgesIndex or AnnotationCornerStyleRoundedIndex))
        {
            return;
        }

        MarkAnnotationPresetCustom();
        AnnotationCornerRadius = cornerStyleIndex == AnnotationCornerStyleRoundedIndex
            ? AnnotationCornerRadiusDefault
            : 0;
        await ApplySelectedAnnotationStyleAsync("Style Annotation", cancellationToken, SelectionStyleAspect.Corners)
            .ConfigureAwait(true);
    }

    public async Task UpdateAnnotationOpacityAsync(
        double opacity,
        CancellationToken cancellationToken)
    {
        MarkAnnotationPresetCustom();
        AnnotationOpacity = opacity;
        await ApplySelectedAnnotationStyleAsync("Style Annotation", cancellationToken, SelectionStyleAspect.Opacity)
            .ConfigureAwait(true);
    }

    public async Task UpdateAnnotationTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1) return;
        AnnotationText = text;

        if (_currentDocument is null
            || _documentRepository is null
            || _annotationEditor is null
            || _annotationSelection.SelectedAnnotationId is not Guid selectedId)
        {
            return;
        }

        AnnotationObject? annotation = _currentDocument.Annotations
            .FirstOrDefault(candidate => candidate.Id == selectedId);
        if (annotation is null)
        {
            _annotationSelection.Clear();
            RefreshAnnotationOverlays();
            NotifyEditorStateChanged();
            return;
        }

        if (annotation.Kind != AnnotationKind.Text
            || string.Equals(annotation.Text, AnnotationText, StringComparison.Ordinal))
        {
            return;
        }

        await _annotationEditor.SetTextAsync(
            _currentDocument,
            annotation,
            AnnotationText,
            cancellationToken,
            TextAnnotationLayout.FitBounds(annotation.Bounds, AnnotationText, annotation.Style.StrokeThickness, annotation.Style.FontFamily)).ConfigureAwait(true);

        if (!SelectedAnnotationIds.Contains(annotation.Id)) _annotationSelection.Select(_currentDocument, annotation.Id);
        await SaveCurrentDocumentAsync("Edit Text complete.", cancellationToken)
            .ConfigureAwait(true);
    }

    public void UpdateAnnotationTool(int toolIndex)
    {
        _activeStamp = null;
        _pixelSelectionRequestVersion++;
        IsSelectionToolActive = false;
        OnPropertyChanged(nameof(IsSelectionToolActive));
        OnPropertyChanged(nameof(IsMarqueeToolActive));
        OnPropertyChanged(nameof(IsMoveToolActive));
        OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
        OnPropertyChanged(nameof(CanAddActiveAnnotation));
        _annotationSelection.Clear();
        MarkAnnotationPresetCustom();
        AnnotationToolIndex = toolIndex;
        ApplyAnnotationToolDefaultsWhenUnselected();
        RefreshAnnotationOverlays();
        NotifyEditorStateChanged();
        OnPropertyChanged(nameof(FillToleranceControlsVisibility));
        StatusText = $"Tool: {ActiveAnnotationKind}.";
    }

    public void UpdateAnnotationPreset(int presetIndex)
    {
        if (presetIndex > 0)
        {
            _activeStamp = null;
            IsSelectionToolActive = false;
            OnPropertyChanged(nameof(IsSelectionToolActive));
            OnPropertyChanged(nameof(IsMarqueeToolActive));
            OnPropertyChanged(nameof(IsMoveToolActive));
            OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
            OnPropertyChanged(nameof(CanAddActiveAnnotation));
        }
        if (presetIndex < 0 || presetIndex > 6)
        {
            return;
        }

        if (presetIndex == 0)
        {
            AnnotationPresetIndex = 0;
            StatusText = "Annotation preset: Custom.";
            return;
        }

        if (_annotationSelection.HasSelection)
        {
            _annotationSelection.Clear();
            RefreshAnnotationOverlays();
            NotifyEditorStateChanged();
        }

        AnnotationPresetIndex = presetIndex;
        ApplyAnnotationPresetDefaults(presetIndex);
        StatusText = $"Annotation preset: {ResolveAnnotationPresetName(presetIndex)}.";
    }

    public bool CaptureNoticeIsOpen
    {
        get => _captureNoticeIsOpen;
        set
        {
            if (_captureNoticeIsOpen == value)
            {
                return;
            }

            _captureNoticeIsOpen = value;
            OnPropertyChanged(nameof(CaptureNoticeIsOpen));
        }
    }

    public string CaptureNoticeTitle
    {
        get => _captureNoticeTitle;
        private set
        {
            if (_captureNoticeTitle == value)
            {
                return;
            }

            _captureNoticeTitle = value;
            OnPropertyChanged(nameof(CaptureNoticeTitle));
        }
    }

    public string CaptureNoticeMessage
    {
        get => _captureNoticeMessage;
        private set
        {
            if (_captureNoticeMessage == value)
            {
                return;
            }

            _captureNoticeMessage = value;
            OnPropertyChanged(nameof(CaptureNoticeMessage));
        }
    }

    public InfoBarSeverity CaptureNoticeSeverity
    {
        get => _captureNoticeSeverity;
        private set
        {
            if (_captureNoticeSeverity == value)
            {
                return;
            }

            _captureNoticeSeverity = value;
            OnPropertyChanged(nameof(CaptureNoticeSeverity));
        }
    }

    public string StorageRoot
    {
        get => _storageRoot;
        private set
        {
            if (_storageRoot == value)
            {
                return;
            }

            _storageRoot = value;
            OnPropertyChanged(nameof(StorageRoot));
        }
    }

    public ApplicationStorageBackend StorageBackend
    {
        get => _storageBackend;
        private set
        {
            if (_storageBackend == value)
            {
                return;
            }

            _storageBackend = value;
            OnPropertyChanged(nameof(StorageBackend));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_settingsStore is null
            || _documentCatalog is null
            || _documentRepository is null
            || _documentWorkspaceBootstrapper is null
            || _documentRenderer is null
            || _diagnosticLog is null)
        {
            return;
        }

        ApplicationSettings settings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);

        CaptureHotkeyIndex = ResolveCaptureHotkeyIndex(settings.CaptureHotkey);
        IncludeCursor = settings.IncludeCursorByDefault;
        CopyCapturesToClipboard = settings.CopyCapturesToClipboard;
        StorageRoot = settings.StorageRoot;
        StorageBackend = settings.StorageBackend;
        InitializeFirstRunSetup(settings);
        await LoadToolDesignsAsync(cancellationToken).ConfigureAwait(true);
        await LoadStampsAsync(cancellationToken).ConfigureAwait(true);
        await _documentWorkspaceBootstrapper
            .EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(true);

        await LoadHistoryFoldersAsync(cancellationToken).ConfigureAwait(true);
        await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
        await OpenMostRecentDocumentAsync(cancellationToken).ConfigureAwait(true);

        bool stillCaptureEnabled = _featureFlags?.IsEnabled(StillCaptureFeatureFlag) == true;
        OnPropertyChanged(nameof(ScrollingCaptureControlsVisibility));
        OnPropertyChanged(nameof(CanCaptureScrolling));
        OnPropertyChanged(nameof(ScreenRecordingControlsVisibility));
        NotifyScreenRecordingStateChanged();
        StatusText = stillCaptureEnabled
            ? "Ready. Listening for capture host messages."
            : "Ready. Still capture is disabled.";

        if (stillCaptureEnabled)
        {
            await RegisterCaptureHotkeyAsync(settings, cancellationToken).ConfigureAwait(true);
        }

        await _diagnosticLog
            .WriteAsync(
                DiagnosticEvent.Create(
                    DiagnosticSeverity.Information,
                    "SnapStudio.App.Shell",
                    "Shell loaded.",
                    new Dictionary<string, string>
                    {
                        ["storageRoot"] = settings.StorageRoot,
                        ["storageBackend"] = settings.StorageBackend.ToString(),
                        ["stillCaptureEnabled"] = stillCaptureEnabled.ToString()
                    }),
                cancellationToken)
            .ConfigureAwait(true);

        await _settingsStore
            .SaveAsync(settings, cancellationToken)
            .ConfigureAwait(true);
    }

    public void OpenFirstRunSetup()
    {
        SettingsSurfaceIsFirstRun = false;
        SettingsThemeIndex = (int)ThemePreference;
        FirstRunCaptureHotkeyIndex = CaptureHotkeyIndex;
        FirstRunIncludeCursor = IncludeCursor;
        FirstRunCopyCapturesToClipboard = CopyCapturesToClipboard;
        FirstRunSetupIsOpen = true;
    }

    public void CloseSettingsSurface()
    {
        if (SettingsSurfaceIsFirstRun)
        {
            return;
        }

        FirstRunSetupIsOpen = false;
    }

    public void UpdateFirstRunCaptureHotkey(int hotkeyIndex)
    {
        if (hotkeyIndex < 0 || hotkeyIndex >= CaptureHotkeyOptions.Length)
        {
            return;
        }

        FirstRunCaptureHotkeyIndex = hotkeyIndex;
    }

    public void UpdateFirstRunStorageBackend(int storageBackendIndex)
    {
        if (storageBackendIndex is not StorageBackendFileSystemIndex
            and not StorageBackendDatabaseIndex)
        {
            return;
        }

        FirstRunStorageBackendIndex = storageBackendIndex;
    }

    public void UpdateScreenRecordingAudioMode(int audioModeIndex)
    {
        if (IsScreenRecording || _screenRecordingOperationInProgress)
        {
            return;
        }

        ScreenRecordingAudioModeIndex = audioModeIndex;
    }

    public async Task ChooseFirstRunStorageRootAsync(CancellationToken cancellationToken)
    {
        if (_storageLocationPicker is null)
        {
            StatusText = "Storage picker is unavailable.";
            ShowCaptureNotice(
                "Storage unavailable",
                "Storage location selection is not available in this session.",
                InfoBarSeverity.Warning);
            return;
        }

        string? selectedPath = await _storageLocationPicker
            .PickStorageRootAsync(FirstRunStorageRoot, cancellationToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            StatusText = "Storage selection cancelled.";
            return;
        }

        FirstRunStorageRoot = selectedPath;
        StatusText = "Storage location selected.";
    }

    public async Task CompleteFirstRunSetupAsync(CancellationToken cancellationToken)
    {
        if (_settingsStore is null)
        {
            return;
        }

        int hotkeyIndex = FirstRunCaptureHotkeyIndex;
        if (hotkeyIndex < 0 || hotkeyIndex >= CaptureHotkeyOptions.Length)
        {
            hotkeyIndex = 0;
        }

        if (hotkeyIndex != CaptureHotkeyIndex)
        {
            await UpdateCaptureHotkeyAsync(hotkeyIndex, cancellationToken).ConfigureAwait(true);
            if (hotkeyIndex != CaptureHotkeyIndex)
            {
                return;
            }
        }

        ApplicationSettings settings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);
        string storageRoot = string.IsNullOrWhiteSpace(FirstRunStorageRoot)
            ? settings.StorageRoot
            : FirstRunStorageRoot;
        ApplicationStorageBackend storageBackend = ResolveStorageBackend(FirstRunStorageBackendIndex);
        bool storageRootChanged = !string.Equals(
            storageRoot,
            StorageRoot,
            StringComparison.OrdinalIgnoreCase);
        bool storageBackendChanged = storageBackend != StorageBackend;
        Dictionary<string, bool> updatedFeatureFlags = CreateSettingsFeatureFlags(settings.FeatureFlags);
        bool featureFlagsChanged = FeatureFlagsDiffer(settings.FeatureFlags, updatedFeatureFlags);
        bool isFirstRun = SettingsSurfaceIsFirstRun;
        ApplicationSettings updatedSettings = settings with
        {
            StorageRoot = storageRoot,
            CaptureHotkey = CaptureHotkeyOptions[CaptureHotkeyIndex],
            IncludeCursorByDefault = FirstRunIncludeCursor,
            CopyCapturesToClipboard = FirstRunCopyCapturesToClipboard,
            FirstRunCompleted = true,
            StorageBackend = storageBackend,
            Theme = (ApplicationThemePreference)SettingsThemeIndex,
            FeatureFlags = updatedFeatureFlags
        };

        await _settingsStore
            .SaveAsync(updatedSettings, cancellationToken)
            .ConfigureAwait(true);

        IncludeCursor = FirstRunIncludeCursor;
        ThemePreference = updatedSettings.Theme;
        CopyCapturesToClipboard = FirstRunCopyCapturesToClipboard;
        FirstRunSetupIsOpen = false;

        if (storageRootChanged || storageBackendChanged || featureFlagsChanged)
        {
            StatusText = isFirstRun
                ? "Setup saved. Restart SnapStudio to apply storage or feature gate changes."
                : "Settings saved. Restart SnapStudio to apply storage or feature gate changes.";
            ShowCaptureNotice(
                isFirstRun ? "Setup saved" : "Settings saved",
                "Restart SnapStudio to apply storage or feature gate changes.",
                InfoBarSeverity.Success);
            return;
        }

        StatusText = isFirstRun
            ? "Setup complete."
            : "Settings saved.";
        ShowCaptureNotice(
            isFirstRun ? "Setup complete" : "Settings saved",
            isFirstRun ? "SnapStudio setup has been saved." : "SnapStudio settings have been saved.",
            InfoBarSeverity.Success);
    }

    public async Task ExportSettingsAsync(CancellationToken cancellationToken)
    {
        if (_settingsStore is null
            || _settingsImportExportService is null
            || _settingsFilePicker is null)
        {
            ShowCaptureNotice(
                "Settings export unavailable",
                "Settings export is not available in this session.",
                InfoBarSeverity.Warning);
            return;
        }

        string? destinationPath = await _settingsFilePicker
            .PickSettingsExportPathAsync("SnapStudio-settings.json", cancellationToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            StatusText = "Settings export cancelled.";
            return;
        }

        ApplicationSettings settings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);
        SettingsExportResult result = await _settingsImportExportService
            .ExportAsync(settings, destinationPath, cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = $"Settings exported: {destinationPath}";
            ShowCaptureNotice(
                "Settings exported",
                $"Settings were exported to {destinationPath}.",
                InfoBarSeverity.Success);
            return;
        }

        string message = CreateSettingsTransferFailureMessage(result.Failure);
        StatusText = $"Settings export failed: {message}";
        ShowCaptureNotice(
            "Settings export failed",
            message,
            InfoBarSeverity.Error);
    }

    public async Task OpenPrivacyNoticeAsync(CancellationToken cancellationToken)
    {
        if (_workspaceShellService is null)
        {
            ShowCaptureNotice(
                "Privacy notes unavailable",
                "Privacy notes cannot be opened in this session.",
                InfoBarSeverity.Warning);
            return;
        }

        string privacyPath = ResolvePrivacyDocumentPath();
        WorkspaceShellResult result = await _workspaceShellService
            .OpenPathAsync(privacyPath, cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = "Privacy notes opened.";
            ShowCaptureNotice(
                "Privacy notes opened",
                "SnapStudio privacy notes were opened in the default viewer.",
                InfoBarSeverity.Informational);
            return;
        }

        string message = result.ErrorMessage ?? "The privacy notes could not be opened.";
        StatusText = $"Privacy notes unavailable: {message}";
        ShowCaptureNotice(
            "Privacy notes unavailable",
            message,
            InfoBarSeverity.Warning);
    }

    public async Task ImportSettingsAsync(CancellationToken cancellationToken)
    {
        if (_settingsStore is null
            || _settingsImportExportService is null
            || _settingsFilePicker is null)
        {
            ShowCaptureNotice(
                "Settings import unavailable",
                "Settings import is not available in this session.",
                InfoBarSeverity.Warning);
            return;
        }

        string? sourcePath = await _settingsFilePicker
            .PickSettingsImportPathAsync(cancellationToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            StatusText = "Settings import cancelled.";
            return;
        }

        ApplicationSettings currentSettings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);
        SettingsImportResult importResult = await _settingsImportExportService
            .ImportAsync(sourcePath, cancellationToken)
            .ConfigureAwait(true);
        if (!importResult.Succeeded || importResult.Settings is null)
        {
            string message = CreateSettingsTransferFailureMessage(importResult.Failure);
            StatusText = $"Settings import failed: {message}";
            ShowCaptureNotice(
                "Settings import failed",
                message,
                InfoBarSeverity.Error);
            return;
        }

        ApplicationSettings importedSettings = NormalizeImportedSettings(importResult.Settings);
        int importedHotkeyIndex = ResolveCaptureHotkeyIndex(importedSettings.CaptureHotkey);
        if (importedHotkeyIndex != CaptureHotkeyIndex)
        {
            await UpdateCaptureHotkeyAsync(importedHotkeyIndex, cancellationToken).ConfigureAwait(true);
            if (importedHotkeyIndex != CaptureHotkeyIndex)
            {
                return;
            }
        }

        await _settingsStore
            .SaveAsync(importedSettings, cancellationToken)
            .ConfigureAwait(true);
        ApplyImportedSettings(importedSettings);

        bool restartRecommended = !string.Equals(
                currentSettings.StorageRoot,
                importedSettings.StorageRoot,
                StringComparison.OrdinalIgnoreCase)
            || currentSettings.StorageBackend != importedSettings.StorageBackend
            || FeatureFlagsDiffer(currentSettings.FeatureFlags, importedSettings.FeatureFlags);

        if (restartRecommended)
        {
            StatusText = "Settings imported. Restart SnapStudio to apply storage or feature flag changes.";
            ShowCaptureNotice(
                "Settings imported",
                "Restart SnapStudio to apply storage or feature flag changes.",
                InfoBarSeverity.Success);
            return;
        }

        StatusText = "Settings imported.";
        ShowCaptureNotice(
            "Settings imported",
            "Settings were imported successfully.",
            InfoBarSeverity.Success);
    }

    public Task CaptureAsync(CancellationToken cancellationToken)
    {
        return CaptureAsync(CaptureMode.Picker, cancellationToken);
    }

    public async Task CaptureAsync(CaptureMode captureMode, CancellationToken cancellationToken)
    {
        if (_captureWorkflow is null || _documentCatalog is null || _diagnosticLog is null)
        {
            return;
        }

        if (_featureFlags?.IsEnabled(StillCaptureFeatureFlag) != true)
        {
            StatusText = "Still capture is disabled.";
            ShowCaptureNotice(
                "Capture disabled",
                "Still capture is disabled by the current feature flags.",
                InfoBarSeverity.Warning);
            return;
        }

        if (_captureInProgress)
        {
            StatusText = "Capture is already in progress.";
            return;
        }

        CaptureNoticeIsOpen = false;
        StatusText = CreateCapturePrompt(captureMode, CaptureDelay);
        SetCaptureInProgress(true);

        try
        {
            CaptureWorkflowResult result = await _captureWorkflow
                .CaptureAsync(
                    new CaptureWorkflowRequest(
                        ResolveAllowedTargets(captureMode),
                        IncludeCursor,
                        CaptureDelay,
                        ResolveSelectionMode(captureMode)),
                    cancellationToken)
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
                bool copiedToClipboard = false;
                if (result.DocumentId is DocumentId documentId)
                {
                    await OpenDocumentAsync(documentId, cancellationToken).ConfigureAwait(true);
                    copiedToClipboard = await CopyCapturedDocumentIfNeededAsync(
                        documentId,
                        cancellationToken)
                        .ConfigureAwait(true);
                }

                StatusText = copiedToClipboard
                    ? $"Capture saved and copied: {result.DocumentId}"
                    : $"Capture saved: {result.DocumentId}";
            }
            else if (result.Failure?.Reason == CaptureFailureReason.Cancelled)
            {
                StatusText = "Capture cancelled.";
                ShowCaptureNotice(
                    "Capture cancelled",
                    "No capture target was selected.",
                    InfoBarSeverity.Informational);
            }
            else
            {
                string failureMessage = result.Failure?.Message ?? "Unknown error.";
                StatusText = $"Capture failed: {failureMessage}";
                ShowCaptureNotice(
                    "Capture failed",
                    CreateCaptureRecoveryMessage(result.Failure),
                    InfoBarSeverity.Error);
            }

            await _diagnosticLog
                .WriteAsync(
                    DiagnosticEvent.Create(
                        result.Succeeded ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                        "SnapStudio.App.Capture",
                        result.Succeeded ? "Capture workflow completed." : "Capture workflow failed.",
                        new Dictionary<string, string>
                        {
                            ["succeeded"] = result.Succeeded.ToString(),
                            ["documentId"] = result.DocumentId?.ToString() ?? string.Empty,
                            ["failureReason"] = result.Failure?.Reason.ToString() ?? string.Empty,
                            ["failureMessage"] = result.Failure?.Message ?? string.Empty
                        }),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Capture cancelled.";
            ShowCaptureNotice(
                "Capture cancelled",
                "The capture operation was cancelled before a frame was saved.",
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            StatusText = $"Capture failed: {exception.Message}";
            ShowCaptureNotice(
                "Capture failed",
                "The capture operation failed before a frame was saved. Try another window or display.",
                InfoBarSeverity.Error);
            await _diagnosticLog
                .WriteAsync(
                    DiagnosticEvent.Create(
                        DiagnosticSeverity.Error,
                        "SnapStudio.App.Capture",
                        "Capture workflow threw an exception.",
                        new Dictionary<string, string>
                        {
                            ["exceptionType"] = exception.GetType().Name,
                            ["message"] = exception.Message
                        }),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            SetCaptureInProgress(false);
        }
    }

    public async Task CaptureScrollingAsync(CancellationToken cancellationToken)
    {
        if (_scrollTargetDetector is null
            || _scrollingCaptureService is null
            || _documentRepository is null
            || _documentCatalog is null
            || _diagnosticLog is null)
        {
            return;
        }

        if (!ScrollingCaptureIsEnabled)
        {
            StatusText = "Scrolling capture is disabled.";
            ShowCaptureNotice(
                "Scrolling capture disabled",
                "Scrolling capture is disabled by the current feature flags.",
                InfoBarSeverity.Warning);
            return;
        }

        if (_captureInProgress)
        {
            StatusText = "Capture is already in progress.";
            return;
        }

        CaptureNoticeIsOpen = false;
        StatusText = "Detecting scroll target.";
        SetCaptureInProgress(true);

        try
        {
            IReadOnlyList<ScrollTargetCandidate> targets = await _scrollTargetDetector
                .DetectAsync(new ScrollTargetDetectionRequest(), cancellationToken)
                .ConfigureAwait(true);

            if (targets.Count == 0)
            {
                StatusText = "No scrollable target found.";
                ShowCaptureNotice(
                    "Scrolling capture unavailable",
                    "No scrollable target was found. Try focusing a scrollable window and run scrolling capture again.",
                    InfoBarSeverity.Warning);
                await WriteScrollingCaptureDiagnosticAsync(
                        false,
                        null,
                        "NoTarget",
                        "No scrollable target was found.",
                        new Dictionary<string, string>(),
                        cancellationToken)
                    .ConfigureAwait(true);
                return;
            }

            ScrollTargetCandidate target = targets[0];
            StatusText = $"Capturing scroll target: {target.DisplayName}.";
            ScrollingCaptureResult result = await _scrollingCaptureService
                .CaptureAsync(
                    new ScrollingCaptureRequest(
                        target,
                        CreateScrollingCaptureOutputDirectory()),
                    cancellationToken)
                .ConfigureAwait(true);

            if (result.HasOutput && result.Image is ImageAsset image)
            {
                CaptureDocument document = await SaveScrollingCaptureDocumentAsync(
                        target,
                        result,
                        image,
                        cancellationToken)
                    .ConfigureAwait(true);
                await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
                await OpenDocumentAsync(document.Id, cancellationToken).ConfigureAwait(true);

                if (result.Succeeded)
                {
                    StatusText = $"Scrolling capture saved: {document.Id}";
                    ShowCaptureNotice(
                        "Scrolling capture saved",
                        $"Captured {result.Frames.Count} scrolling frame(s) from {target.DisplayName}.",
                        InfoBarSeverity.Success);
                }
                else
                {
                    StatusText = $"Partial scrolling capture saved: {document.Id}";
                    ShowCaptureNotice(
                        "Partial scrolling capture saved",
                        CreateScrollingPartialMessage(result),
                        InfoBarSeverity.Warning);
                }
            }
            else
            {
                string message = result.Failure?.Message ?? "Scrolling capture did not produce an image.";
                StatusText = $"Scrolling capture failed: {message}";
                ShowCaptureNotice(
                    "Scrolling capture failed",
                    CreateScrollingFailureMessage(result),
                    InfoBarSeverity.Error);
            }

            await WriteScrollingCaptureDiagnosticAsync(
                    result.Succeeded,
                    result.Image is not null
                        ? result.Frames.Count.ToString(CultureInfo.InvariantCulture)
                        : null,
                    result.Failure?.Reason.ToString() ?? string.Empty,
                    result.Failure?.Message ?? string.Empty,
                    result.Diagnostics,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Scrolling capture cancelled.";
            ShowCaptureNotice(
                "Scrolling capture cancelled",
                "The scrolling capture operation was cancelled before output was saved.",
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            StatusText = $"Scrolling capture failed: {exception.Message}";
            ShowCaptureNotice(
                "Scrolling capture failed",
                "The scrolling capture operation failed before output was saved. Try another scrollable target.",
                InfoBarSeverity.Error);
            await _diagnosticLog
                .WriteAsync(
                    DiagnosticEvent.Create(
                        DiagnosticSeverity.Error,
                        "SnapStudio.App.ScrollingCapture",
                        "Scrolling capture workflow threw an exception.",
                        new Dictionary<string, string>
                        {
                            ["exceptionType"] = exception.GetType().Name,
                            ["message"] = exception.Message
                        }),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            SetCaptureInProgress(false);
        }
    }

    public Task ToggleScreenRecordingAsync(CancellationToken cancellationToken)
    {
        return IsScreenRecording
            ? StopScreenRecordingAsync(cancellationToken)
            : StartScreenRecordingAsync(cancellationToken);
    }

    private async Task StartScreenRecordingAsync(CancellationToken cancellationToken)
    {
        if (_captureTargetSelector is null || _screenRecordingService is null)
        {
            return;
        }

        if (!ScreenRecordingIsEnabled)
        {
            StatusText = "Screen recording is disabled.";
            ShowCaptureNotice(
                "Recording disabled",
                "Screen recording is disabled by the current feature flags.",
                InfoBarSeverity.Warning);
            return;
        }

        if (_captureInProgress)
        {
            StatusText = "Capture is already in progress.";
            return;
        }

        if (_screenRecordingOperationInProgress)
        {
            return;
        }

        CaptureNoticeIsOpen = false;
        StatusText = "Choose a window or display to record.";
        SetScreenRecordingOperationInProgress(true);

        try
        {
            CaptureTargetSelection? selection = await _captureTargetSelector
                .SelectTargetAsync(
                    new CaptureTargetRequest(
                        [CaptureTargetKind.Display, CaptureTargetKind.Window],
                        AllowDelayedCapture: false,
                        CaptureTargetSelectionMode.Picker),
                    cancellationToken)
                .ConfigureAwait(true);

            if (selection is null)
            {
                StatusText = "Recording cancelled.";
                ShowCaptureNotice(
                    "Recording cancelled",
                    "No recording target was selected.",
                    InfoBarSeverity.Informational);
                await WriteScreenRecordingDiagnosticAsync(
                        false,
                        "Screen recording target selection was cancelled.",
                        string.Empty,
                        new ScreenRecordingFailure(
                            ScreenRecordingFailureReason.Cancelled,
                            "No recording target was selected."),
                        new Dictionary<string, string>(),
                        cancellationToken)
                    .ConfigureAwait(true);
                return;
            }

            string outputPath = CreateScreenRecordingOutputPath();
            (bool includeMicrophoneAudio, bool includeSystemAudio) = CreateScreenRecordingAudioFlags();
            Dictionary<string, string> requestDiagnostics = CreateScreenRecordingRequestDiagnostics(
                includeMicrophoneAudio,
                includeSystemAudio);
            ScreenRecordingStartResult result = await _screenRecordingService
                .StartAsync(
                    new ScreenRecordingStartRequest(
                        selection.TargetKind,
                        outputPath,
                        IncludeCursor,
                        IncludeMicrophoneAudio: includeMicrophoneAudio,
                        IncludeSystemAudio: includeSystemAudio,
                        TargetHint: selection.TargetId,
                        Bounds: selection.Bounds,
                        TargetMetadata: selection.Metadata),
                    cancellationToken)
                .ConfigureAwait(true);

            if (result.Succeeded && result.Session is ScreenRecordingSession session)
            {
                _activeScreenRecordingSession = session;
                NotifyScreenRecordingStateChanged();
                StatusText = "Recording started.";
                ShowCaptureNotice(
                    "Recording started",
                    "Use Capture > Stop Recording to finish and save the MP4.",
                    InfoBarSeverity.Success);
            }
            else
            {
                StatusText = $"Recording failed: {result.Failure?.Message ?? "Unknown error."}";
                ShowCaptureNotice(
                    "Recording failed",
                    CreateScreenRecordingFailureMessage(result.Failure),
                    InfoBarSeverity.Error);
            }

            await WriteScreenRecordingDiagnosticAsync(
                    result.Succeeded,
                    result.Succeeded
                        ? "Screen recording started."
                        : "Screen recording failed to start.",
                    result.Session?.OutputPath ?? outputPath,
                    result.Failure,
                    MergeScreenRecordingDiagnostics(requestDiagnostics, result.Session?.Metadata),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Recording cancelled.";
            ShowCaptureNotice(
                "Recording cancelled",
                "The recording operation was cancelled before it started.",
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            StatusText = $"Recording failed: {exception.Message}";
            ShowCaptureNotice(
                "Recording failed",
                "The recording operation failed before it started. Try another window or display.",
                InfoBarSeverity.Error);
            await WriteScreenRecordingDiagnosticAsync(
                    false,
                    "Screen recording start threw an exception.",
                    string.Empty,
                    new ScreenRecordingFailure(
                        ScreenRecordingFailureReason.Unknown,
                        exception.Message,
                        exception),
                    new Dictionary<string, string>
                    {
                        ["exceptionType"] = exception.GetType().Name
                    },
                    cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            SetScreenRecordingOperationInProgress(false);
        }
    }

    private async Task StopScreenRecordingAsync(CancellationToken cancellationToken)
    {
        if (_screenRecordingService is null || _activeScreenRecordingSession is null)
        {
            return;
        }

        if (_screenRecordingOperationInProgress)
        {
            return;
        }

        ScreenRecordingSession session = _activeScreenRecordingSession;
        CaptureNoticeIsOpen = false;
        StatusText = "Stopping recording.";
        SetScreenRecordingOperationInProgress(true);

        try
        {
            ScreenRecordingStopResult result = await _screenRecordingService
                .StopAsync(session.Id, cancellationToken)
                .ConfigureAwait(true);

            if (result.Succeeded && result.Output is ScreenRecordingOutput output)
            {
                _activeScreenRecordingSession = null;
                NotifyScreenRecordingStateChanged();
                StatusText = $"Recording saved: {output.Path}";
                ShowCaptureNotice(
                    "Recording saved",
                    $"Saved MP4 recording: {output.Path}",
                    InfoBarSeverity.Success);
            }
            else if (result.Output is ScreenRecordingOutput partialOutput)
            {
                _activeScreenRecordingSession = null;
                NotifyScreenRecordingStateChanged();
                StatusText = $"Recording saved with warnings: {partialOutput.Path}";
                ShowCaptureNotice(
                    "Recording saved with warnings",
                    CreateScreenRecordingFailureMessage(result.Failure),
                    InfoBarSeverity.Warning);
            }
            else
            {
                _activeScreenRecordingSession = null;
                NotifyScreenRecordingStateChanged();
                StatusText = $"Recording failed: {result.Failure?.Message ?? "Unknown error."}";
                ShowCaptureNotice(
                    "Recording failed",
                    CreateScreenRecordingFailureMessage(result.Failure),
                    InfoBarSeverity.Error);
            }

            await WriteScreenRecordingDiagnosticAsync(
                    result.Succeeded,
                    result.Succeeded
                        ? "Screen recording stopped."
                        : "Screen recording stopped with failure state.",
                    result.Output?.Path ?? session.OutputPath,
                    result.Failure,
                    result.Diagnostics,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Recording stop cancelled.";
            ShowCaptureNotice(
                "Recording stop cancelled",
                "The recording is still active.",
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            StatusText = $"Recording failed: {exception.Message}";
            ShowCaptureNotice(
                "Recording failed",
                "The recording operation failed while stopping. Try stopping it again.",
                InfoBarSeverity.Error);
            await WriteScreenRecordingDiagnosticAsync(
                    false,
                    "Screen recording stop threw an exception.",
                    session.OutputPath,
                    new ScreenRecordingFailure(
                        ScreenRecordingFailureReason.Unknown,
                        exception.Message,
                        exception),
                    new Dictionary<string, string>
                    {
                        ["exceptionType"] = exception.GetType().Name
                    },
                    cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            SetScreenRecordingOperationInProgress(false);
        }
    }

    public async Task OpenDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(documentId, out Guid parsed))
        {
            StatusText = "Document could not be opened.";
            return;
        }

        await OpenDocumentAsync(new DocumentId(parsed), cancellationToken).ConfigureAwait(true);
    }

    public Task OpenImageFileAsync(CancellationToken cancellationToken) => CanOpenImageFile
        ? OpenImageSourceAsync(_imageImportService!.ImportAsync, "Open", cancellationToken)
        : Task.CompletedTask;

    public Task PasteImageAsync(CancellationToken cancellationToken) => HasCurrentDocument
        ? PasteImageAsLayerAsync(cancellationToken)
        : PasteImageAsNewDocumentAsync(cancellationToken);

    public Task PasteImageAsNewDocumentAsync(CancellationToken cancellationToken) => CanCreateImage
        ? OpenImageSourceAsync(_imageCreationService!.PasteAsync, "Paste", cancellationToken)
        : Task.CompletedTask;

    public async Task PasteImageAsLayerAsync(CancellationToken cancellationToken)
    {
        if (!CanPasteImageAsLayer || _currentDocument is not CaptureDocument document) return;
        SetCaptureInProgress(true);
        CaptureNoticeIsOpen = false;
        try
        {
            ClipboardImageResult result = await _imageCreationService!.ReadClipboardImageAsync(cancellationToken).ConfigureAwait(true);
            if (result.Image is not AnnotationImage image)
            {
                string message = result.ErrorMessage ?? "The clipboard image could not be pasted.";
                StatusText = $"Paste failed: {message}";
                ShowCaptureNotice("Paste failed", message, result.Exception is null ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
                return;
            }

            if (!ReferenceEquals(_currentDocument, document))
            {
                StatusText = "The open image changed. Paste again to add a layer to this image.";
                return;
            }

            AnnotationObject annotation = await _annotationEditor!.PasteImageAsLayerAsync(document, image, cancellationToken).ConfigureAwait(true);
            UseSelectionTool();
            SelectAnnotation(annotation.Id);
            await SaveCurrentDocumentAsync("Image pasted as a new layer. Drag to move or resize it.", cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Paste cancelled.";
        }
        catch (Exception exception)
        {
            StatusText = $"Paste failed: {exception.Message}";
            ShowCaptureNotice("Paste failed", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetCaptureInProgress(false);
        }
    }

    public Task CreateBlankImageAsync(NewImageRequest request, CancellationToken cancellationToken) => CanCreateImage
        ? OpenImageSourceAsync(
            token => _imageCreationService!.CreateBlankAsync(request, token),
            "New image",
            cancellationToken)
        : Task.CompletedTask;

    private async Task OpenImageSourceAsync(
        Func<CancellationToken, Task<CaptureOutcome>> getImage,
        string operation,
        CancellationToken cancellationToken)
    {
        SetCaptureInProgress(true);
        CaptureNoticeIsOpen = false;
        try
        {
            CaptureOutcome outcome = await getImage(cancellationToken).ConfigureAwait(true);
            if (!outcome.Succeeded || outcome.Capture is not CaptureResult capture)
            {
                if (outcome.Failure?.Reason == CaptureFailureReason.Cancelled)
                {
                    StatusText = $"{operation} cancelled.";
                    return;
                }

                string message = outcome.Failure?.Message ?? "The image could not be opened.";
                StatusText = $"{operation} failed: {message}";
                ShowCaptureNotice($"{operation} failed", message,
                    outcome.Failure?.Exception is null ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
                return;
            }

            CaptureDocument document = await _documentRepository!
                .CreateFromCaptureAsync(capture, cancellationToken)
                .ConfigureAwait(true);
            await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
            await OpenDocumentAsync(document.Id, cancellationToken).ConfigureAwait(true);
            StatusText = $"Opened {ResolveDocumentTitle(document)}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = $"{operation} cancelled.";
        }
        catch (Exception exception)
        {
            StatusText = $"{operation} failed: {exception.Message}";
            ShowCaptureNotice($"{operation} failed", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetCaptureInProgress(false);
        }
    }

    public void SearchRecentDocuments(string searchText)
    {
        _historySearchText = searchText?.Trim() ?? string.Empty;
        _historyPagination.Reset();
        ApplyRecentDocumentFilter(refreshThumbnails: true);
    }

    public void UpdateHistoryFilter(int filterIndex)
    {
        HistoryFilterIndex = filterIndex;
        _historyPagination.Reset();
        ApplyRecentDocumentFilter(refreshThumbnails: true);
    }

    public async Task RenameCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null)
        {
            return;
        }

        CaptureDocumentWorkspaceEditor.SetTitle(_currentDocument, WorkspaceTitleText);
        CurrentDocumentTitle = ResolveDocumentTitle(_currentDocument);
        WorkspaceTitleText = CurrentDocumentTitle;
        await SaveCurrentDocumentAsync("Renamed capture.", cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task DuplicateCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null)
        {
            return;
        }

        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(
            _currentDocument,
            CreateDuplicateTitle(CurrentDocumentTitle));

        await _documentRepository
            .SaveAsync(duplicate, cancellationToken)
            .ConfigureAwait(true);

        await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
        await OpenDocumentAsync(duplicate.Id, cancellationToken).ConfigureAwait(true);
        StatusText = "Duplicated capture.";
    }

    public Task DeleteCurrentDocumentAsync(CancellationToken cancellationToken) =>
        _currentDocumentId is DocumentId id
            ? RemoveCaptureFromHistoryAsync(id.ToString(), cancellationToken) : Task.CompletedTask;

    public Task MoveCurrentCaptureToTrashAsync(CancellationToken cancellationToken) =>
        _currentDocumentId is DocumentId id
            ? MoveCaptureToTrashAsync(id.ToString(), cancellationToken) : Task.CompletedTask;

    private async Task MoveCaptureToTrashCoreAsync(DocumentId documentId, CancellationToken cancellationToken)
    {
        if (_documentRepository is null || _fileTrashService is null)
        {
            return;
        }

        CaptureDocument? document = _currentDocumentId == documentId ? _currentDocument
            : await _documentRepository.GetAsync(documentId, cancellationToken).ConfigureAwait(true);
        if (document is null)
        {
            StatusText = "The capture is no longer available.";
            return;
        }
        string sourceImagePath = document.SourceImage.Path;
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            StatusText = "Source image file could not be found.";
            ShowCaptureNotice(
                "Source image missing",
                "The workspace entry can still be removed from history, but the source file was not found.",
                InfoBarSeverity.Warning);
            return;
        }

        if (await SourceImageIsReferencedByAnotherDocumentAsync(
                documentId,
                sourceImagePath,
                cancellationToken)
            .ConfigureAwait(true))
        {
            StatusText = "Source image is used by another capture.";
            ShowCaptureNotice(
                "Source image is shared",
                "Another capture uses the same source image. Remove the capture from history, or remove the related captures before moving the source image to the recycle bin.",
                InfoBarSeverity.Warning);
            return;
        }

        FileTrashResult trashResult = await _fileTrashService
            .MoveFileToTrashAsync(sourceImagePath, cancellationToken)
            .ConfigureAwait(true);
        if (!trashResult.Succeeded)
        {
            string message = trashResult.ErrorMessage ?? "The source image could not be moved to the recycle bin.";
            StatusText = message;
            ShowCaptureNotice(
                "Trash unavailable",
                message,
                InfoBarSeverity.Error);
            return;
        }

        bool deleted = await DeleteDocumentRecordAsync(
                documentId,
                "Moved capture to recycle bin.",
                cancellationToken)
            .ConfigureAwait(true);
        if (!deleted)
        {
            StatusText = "Source image moved to recycle bin, but the workspace entry could not be removed.";
            ShowCaptureNotice(
                "Workspace entry remains",
                "The source image was moved to the recycle bin, but the workspace entry could not be removed.",
                InfoBarSeverity.Warning);
        }
    }

    private async Task<bool> DeleteDocumentRecordAsync(
        DocumentId documentId,
        string successStatus,
        CancellationToken cancellationToken)
    {
        if (_documentRepository is null)
        {
            return false;
        }

        bool deleted = await _documentRepository
            .DeleteAsync(documentId, cancellationToken)
            .ConfigureAwait(true);

        if (!deleted)
        {
            StatusText = "Document could not be deleted.";
            return false;
        }

        bool wasCurrent = _currentDocumentId == documentId;
        RemoveRecentDocument(documentId);
        if (wasCurrent) await ClearCurrentDocumentAsync(cancellationToken).ConfigureAwait(true);
        await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
        if (wasCurrent) await OpenMostRecentDocumentAsync(cancellationToken).ConfigureAwait(true);
        StatusText = successStatus;
        return true;
    }

    public async Task OpenCurrentDocumentFolderAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _workspaceShellService is null)
        {
            return;
        }

        WorkspaceShellResult result = await _workspaceShellService
            .OpenContainingFolderAsync(_currentDocument.SourceImage.Path, cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = "Opened containing folder.";
            return;
        }

        string message = result.ErrorMessage ?? "The containing folder could not be opened.";
        StatusText = message;
        ShowCaptureNotice(
            "Folder unavailable",
            message,
            InfoBarSeverity.Warning);
    }

    public async Task PinCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_currentDocumentId is null || _pinnedImageService is null)
        {
            ShowCaptureNotice(
                "Nothing to pin",
                "Open or capture an image before pinning.",
                InfoBarSeverity.Warning);
            return;
        }

        PinnedImageResult result = await _pinnedImageService
            .PinDocumentAsync(
                new PinnedImageRequest(_currentDocumentId.Value, CurrentDocumentTitle),
                cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = "Pinned current image.";
            ShowCaptureNotice(
                "Pinned",
                "The current image was pinned in a floating window.",
                InfoBarSeverity.Success);
            return;
        }

        StatusText = $"Pin failed: {result.ErrorMessage}";
        ShowCaptureNotice(
            "Pin failed",
            result.ErrorMessage ?? "The current image could not be pinned.",
            InfoBarSeverity.Error);
    }

    public async Task CopyCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_clipboardService is null || _currentDocumentId is null)
        {
            ShowCaptureNotice(
                "Nothing to copy",
                "Open or capture an image before copying.",
                InfoBarSeverity.Warning);
            return;
        }

        DocumentId documentId = _currentDocumentId.Value;
        try
        {
            await _pendingDocumentSave.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Copy cancelled.";
            return;
        }
        catch (Exception exception)
        {
            StatusText = $"Copy failed: {exception.Message}";
            ShowCaptureNotice("Copy failed", "The latest edits could not be saved. " + exception.Message, InfoBarSeverity.Error);
            return;
        }
        if (_currentDocumentId != documentId) return;
        RectD? selectionBounds = SourceSelectionBounds;
        PixelSelection? selection = SourcePixelSelection;
        ClipboardResult result = selection is null
            ? await _clipboardService.CopyDocumentAsync(documentId, selectionBounds, cancellationToken).ConfigureAwait(true)
            : await _clipboardService.CopySelectionAsync(documentId, selection, cancellationToken).ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = selectionBounds.HasValue ? "Copied selection to clipboard." : "Copied image to clipboard.";
            ShowCaptureNotice(
                "Copied",
                selectionBounds.HasValue ? "The selected area was copied to the clipboard." : "The current image was copied to the clipboard.",
                InfoBarSeverity.Success);
            return;
        }

        StatusText = $"Copy failed: {result.ErrorMessage}";
        ShowCaptureNotice(
            "Copy failed",
            result.ErrorMessage ?? "The current image could not be copied.",
            InfoBarSeverity.Error);
    }

    public async Task CopyRecognizedTextAsync(CancellationToken cancellationToken)
    {
        if (!OcrIsEnabled)
        {
            ShowCaptureNotice(
                "OCR disabled",
                "Text extraction is disabled by the current feature flags.",
                InfoBarSeverity.Warning);
            return;
        }

        if (_currentDocument is null
            || _ocrTextExtractionService is null
            || _clipboardService is null)
        {
            ShowCaptureNotice(
                "Nothing to scan",
                "Open or capture an image before extracting text.",
                InfoBarSeverity.Warning);
            return;
        }

        AnnotationObject? selectedAnnotation = GetSelectedAnnotation();
        RectD? sourceRegion = selectedAnnotation?.Bounds;
        StatusText = sourceRegion is null
            ? "Extracting text from image..."
            : "Extracting text from selected region...";

        OcrTextExtractionResult extractionResult = await _ocrTextExtractionService
            .ExtractAsync(
                new OcrTextExtractionRequest(
                    _currentDocument.Id,
                    _currentDocument.SourceImage,
                    sourceRegion),
                cancellationToken)
            .ConfigureAwait(true);

        if (!extractionResult.Succeeded || extractionResult.Result is not OcrResult ocrResult)
        {
            string message = extractionResult.Failure?.Message ?? "Text could not be extracted.";
            StatusText = $"OCR failed: {message}";
            ShowCaptureNotice("OCR failed", message, InfoBarSeverity.Error);
            return;
        }

        string text = ocrResult.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = extractionResult.WasFromCache
                ? "Cached OCR result has no text."
                : "No text found.";
            ShowCaptureNotice(
                "No text found",
                "No text was found in the selected OCR area.",
                InfoBarSeverity.Informational);
            return;
        }

        ClipboardResult clipboardResult = await _clipboardService
            .CopyTextAsync(text, cancellationToken)
            .ConfigureAwait(true);

        if (clipboardResult.Succeeded)
        {
            StatusText = extractionResult.WasFromCache
                ? "Copied cached recognized text."
                : "Copied recognized text.";
            ShowCaptureNotice(
                "Copied text",
                extractionResult.WasFromCache
                    ? "Cached recognized text was copied to the clipboard."
                    : "Recognized text was copied to the clipboard.",
                InfoBarSeverity.Success);
            return;
        }

        StatusText = $"Copy text failed: {clipboardResult.ErrorMessage}";
        ShowCaptureNotice(
            "Copy text failed",
            clipboardResult.ErrorMessage ?? "Recognized text could not be copied.",
            InfoBarSeverity.Error);
    }

    public async Task ExportCurrentDocumentAsync(
        ExportFormat format,
        CancellationToken cancellationToken)
    {
        if (_currentDocumentId is null
            || _exportDestinationPicker is null
            || _exportProviders is null)
        {
            ShowCaptureNotice(
                "Nothing to export",
                "Open or capture an image before exporting.",
                InfoBarSeverity.Warning);
            return;
        }

        if (!_exportProviders.TryGetValue(format, out IExportProvider? exportProvider))
        {
            ShowCaptureNotice(
                "Export unavailable",
                $"{format} export is not available.",
                InfoBarSeverity.Warning);
            return;
        }

        ExportDestination? destination = await _exportDestinationPicker
            .PickDestinationAsync(format, CreateExportFileName(format), cancellationToken)
            .ConfigureAwait(true);

        if (destination is null)
        {
            StatusText = "Export cancelled.";
            return;
        }

        ExportResult result = await exportProvider
            .ExportAsync(
                new ExportRequest(
                    _currentDocumentId.Value,
                    destination.Format,
                    destination.OutputPath,
                    new Dictionary<string, string>()),
                cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = $"Exported image: {result.OutputPath}";
            ShowCaptureNotice(
                "Exported",
                $"Saved {format} export to {result.OutputPath}.",
                InfoBarSeverity.Success);
            return;
        }

        StatusText = $"Export failed: {result.ErrorMessage}";
        ShowCaptureNotice(
            "Export failed",
            result.ErrorMessage ?? "The current image could not be exported.",
            InfoBarSeverity.Error);
    }

    public void FitCanvasToViewport(double viewportWidth, double viewportHeight)
    {
        if (!_canvasViewport.HasSource)
        {
            return;
        }

        SetCanvasViewport(_canvasViewport.FitTo(viewportWidth, viewportHeight));
        _canvasFitToViewport = true;
    }

    public void RefitCanvasIfNeeded(double viewportWidth, double viewportHeight)
    {
        if (!_canvasFitToViewport)
        {
            return;
        }

        FitCanvasToViewport(viewportWidth, viewportHeight);
    }

    public void ZoomIn() => ZoomWithWheel(120);

    public void ZoomOut() => ZoomWithWheel(-120);

    public void ZoomWithWheel(int wheelDelta)
    {
        if (!_canvasViewport.HasSource || wheelDelta == 0)
        {
            return;
        }

        _canvasFitToViewport = false;
        SetCanvasViewport(_canvasViewport.ZoomWithWheel(wheelDelta));
        StatusText = $"Zoom: {CanvasZoomLabel}.";
    }

    public void ResetZoom()
    {
        if (!_canvasViewport.HasSource)
        {
            return;
        }

        _canvasFitToViewport = false;
        SetCanvasViewport(_canvasViewport.WithZoom(1));
        StatusText = "Zoom: 100%.";
    }

    public async Task AddRectangleAnnotationAsync(CancellationToken cancellationToken)
    {
        await AddAnnotationCoreAsync(AnnotationKind.Rectangle, null, cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task AddRectangleAnnotationAsync(
        RectD sourceBounds,
        CancellationToken cancellationToken)
    {
        await AddAnnotationCoreAsync(AnnotationKind.Rectangle, sourceBounds, cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task AddActiveAnnotationAsync(CancellationToken cancellationToken)
    {
        if (IsSelectionToolActive || IsStampToolActive || ActiveAnnotationKind is AnnotationKind.Pen or AnnotationKind.Fill)
        {
            StatusText = "Choose a shape tool, draw with Pen, or click an area with Fill bucket.";
            return;
        }
        await AddAnnotationCoreAsync(ActiveAnnotationKind, null, cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task AddActiveAnnotationAsync(
        RectD sourceBounds,
        CancellationToken cancellationToken,
        bool bypassPerspectiveSnapping = false)
    {
        if (IsSelectionToolActive || IsStampToolActive || ActiveAnnotationKind is AnnotationKind.Pen or AnnotationKind.Fill)
        {
            return;
        }
        await AddAnnotationCoreAsync(ActiveAnnotationKind, sourceBounds, cancellationToken, bypassPerspectiveSnapping)
            .ConfigureAwait(true);
    }

    public async Task FillAtCanvasPointAsync(PointD displayPoint, CancellationToken cancellationToken)
    {
        if (_fillInProgress || _currentDocument is null || _documentRepository is null
            || _documentRenderer is null || _documentRasterEditor is null || _annotationEditor is null
            || !_canvasViewport.HasSource)
        {
            return;
        }

        CaptureDocument document = _currentDocument;
        int changeVersion = _documentChangeVersion;
        Guid? layerId = _layerWorkspace.ActiveLayerId;
        PointD point = _canvasViewport.ToSourcePoint(displayPoint);
        point = new PointD(Math.Clamp(point.X, 0, document.SourceImage.Width - 1), Math.Clamp(point.Y, 0, document.SourceImage.Height - 1));
        AnnotationStyle style = CreateAnnotationStyle(AnnotationKind.Fill);
        int tolerance = (int)FillTolerance;
        _fillInProgress = true;
        try
        {
            if (ShapeFill.FindTarget(document, point) is AnnotationObject shape)
            {
                bool changed = await _annotationEditor.SetShapeFillAsync(document, shape, style.Fill, style.Opacity, cancellationToken)
                    .ConfigureAwait(true);
                if (!changed)
                {
                    StatusText = "This shape already has that fill.";
                    return;
                }

                _annotationSelection.Clear();
                await SaveCurrentDocumentAsync($"{shape.Kind} filled.", cancellationToken).ConfigureAwait(true);
                return;
            }

            StatusText = "Filling area…";
            RenderResult rendered = await _documentRenderer.RenderAsync(new RenderRequest(document.Id, 1, null), cancellationToken)
                .ConfigureAwait(true);
            if (!rendered.Succeeded || rendered.Image is null)
            {
                StatusText = $"Fill failed: {rendered.ErrorMessage}";
                return;
            }

            RegionFillResult result = await _documentRasterEditor.FindFillRegionAsync(new RegionFillRequest(rendered.Image, point, tolerance), cancellationToken)
                .ConfigureAwait(true);
            if (!result.Succeeded || result.Region is not FillRegion region)
            {
                StatusText = $"Fill failed: {result.ErrorMessage}";
                return;
            }

            if (!ReferenceEquals(document, _currentDocument) || changeVersion != _documentChangeVersion
                || layerId != _layerWorkspace.ActiveLayerId)
            {
                StatusText = "The document or selected layer changed. Click again to fill.";
                return;
            }

            if (style.Fill.A == 0 || (style.Opacity == 1 && style.Fill.A == 255 && region.OriginalColor == style.Fill))
            {
                StatusText = "This area already has that color.";
                return;
            }

            var fill = new AnnotationObject
            {
                Kind = AnnotationKind.Fill,
                Bounds = region.Bounds,
                FillRectangles = [.. region.Rectangles],
                Style = style
            };
            if (!_layerWorkspace.TryAssignActiveLayer(document, fill))
            {
                StatusText = "Show the selected layer before filling.";
                return;
            }

            await _annotationEditor.AddAsync(document, fill, cancellationToken).ConfigureAwait(true);
            _annotationSelection.Clear();
            await SaveCurrentDocumentAsync("Area filled.", cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _fillInProgress = false;
        }
    }

    public async Task AddPenStrokeAsync(IReadOnlyList<PointD> points, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null || points.Count == 0)
        {
            return;
        }

        AnnotationObject stroke = FreehandStroke.Create(points, CreateAnnotationStyle(AnnotationKind.Pen));
        if (!_layerWorkspace.TryAssignActiveLayer(_currentDocument, stroke))
        {
            StatusText = "Show the selected layer before drawing.";
            return;
        }

        await _annotationEditor.AddAsync(_currentDocument, stroke, cancellationToken).ConfigureAwait(true);
        _annotationSelection.Clear();
        await SaveCurrentDocumentAsync("Pen stroke added.", cancellationToken).ConfigureAwait(true);
    }

    public RectD? CreateSourceBoundsFromCanvasDrag(RectD displayBounds)
    {
        if (!_canvasViewport.HasSource)
        {
            return null;
        }

        RectD sourceBounds = _canvasViewport.ToSourceBounds(displayBounds);
        return sourceBounds.Width >= 2 && sourceBounds.Height >= 2
            ? sourceBounds
            : null;
    }

    public RectD? CreateDirectedSourceBoundsFromCanvasDrag(
        PointD displayStart,
        PointD displayEnd)
    {
        if (!_canvasViewport.HasSource)
        {
            return null;
        }

        PointD sourceStart = _canvasViewport.ToSourcePoint(displayStart);
        PointD sourceEnd = _canvasViewport.ToSourcePoint(displayEnd);
        double width = sourceEnd.X - sourceStart.X;
        double height = sourceEnd.Y - sourceStart.Y;
        double length = Math.Sqrt(width * width + height * height);

        return length >= 2
            ? new RectD(sourceStart.X, sourceStart.Y, width, height)
            : null;
    }

    public PointD? CreateSourcePointFromCanvasPoint(PointD displayPoint)
    {
        return _canvasViewport.HasSource
            ? _canvasViewport.ToSourcePoint(displayPoint)
            : null;
    }

    public RectD CreateDisplayBoundsFromSourceBounds(RectD sourceBounds)
    {
        return _canvasViewport.ToDisplayBounds(sourceBounds);
    }

    public RectD? GetAnnotationBounds(Guid annotationId)
    {
        return _currentDocument?.Annotations
            .FirstOrDefault(annotation => annotation.Id == annotationId)
            ?.Bounds;
    }

    public AnnotationOverlayItem? CreateActiveAnnotationPreview(PointD displayStart, PointD displayEnd, bool bypassPerspectiveSnapping = false)
        => CreateActiveAnnotationPreview(displayStart, displayEnd, bypassPerspectiveSnapping, out _);

    public AnnotationOverlayItem? CreateActiveAnnotationPreview(PointD displayStart, PointD displayEnd, bool bypassPerspectiveSnapping,
        out IReadOnlyList<AnnotationSnapGuide> placementGuides)
    {
        placementGuides = [];
        if (_currentDocument is null || !_canvasViewport.HasSource
            || IsSelectionToolActive || ActiveAnnotationKind is AnnotationKind.Pen or AnnotationKind.Fill)
        {
            return null;
        }

        PointD start = _canvasViewport.ToSourcePoint(displayStart);
        PointD end = _canvasViewport.ToSourcePoint(displayEnd);
        AnnotationObject annotation = CreateAnnotation(_currentDocument, ActiveAnnotationKind,
            new RectD(start.X, start.Y, end.X - start.X, end.Y - start.Y));
        if (annotation.Kind == AnnotationKind.Text) placementGuides = SnapNewObjectPlacement(annotation, bypassPerspectiveSnapping);
        return AnnotationOverlayItem.FromAnnotation(annotation, _canvasViewport.Zoom, null);
    }

    public AnnotationOverlayItem? CreateAnnotationEditPreview(Guid annotationId, RectD sourceBounds)
    {
        AnnotationObject? annotation = _currentDocument?.Annotations
            .FirstOrDefault(candidate => candidate.Id == annotationId);
        if (annotation is null || !DocumentLayers.IsEffectivelyVisible(_currentDocument!, annotation)) return null;
        AnnotationObject preview = CloneAnnotation(annotation, sourceBounds, copyImageBytes: false);
        if (annotation.Kind == AnnotationKind.Text && TextSizeChanged(annotation.Bounds, sourceBounds))
            (preview.Bounds, preview.Style) = TextAnnotationLayout.Resize(annotation, sourceBounds);
        return AnnotationOverlayItem.FromAnnotation(preview, _canvasViewport.Zoom, null);
    }

    public RectD? CreateEditedAnnotationBounds(
        AnnotationBoundsHandle handle,
        RectD originalSourceBounds,
        PointD dragStartSourcePoint,
        PointD currentSourcePoint,
        bool constrainRectangleOrEllipse = false)
    {
        if (_currentDocument is null)
        {
            return null;
        }

        var sourceSize = new SizeD(
            _currentDocument.SourceImage.Width,
            _currentDocument.SourceImage.Height);

        if (constrainRectangleOrEllipse
            && handle is not (AnnotationBoundsHandle.Move
                or AnnotationBoundsHandle.Start
                or AnnotationBoundsHandle.End)
            && GetSelectedAnnotation()?.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse
            && TryCreateSquareResizeBounds(
                originalSourceBounds,
                handle,
                currentSourcePoint,
                sourceSize,
                out RectD squareBounds))
        {
            return squareBounds;
        }

        if (handle is AnnotationBoundsHandle.Start or AnnotationBoundsHandle.End)
        {
            return AnnotationBoundsEditor.MoveEndpoint(
                originalSourceBounds,
                handle,
                currentSourcePoint,
                sourceSize);
        }

        if (handle == AnnotationBoundsHandle.Move && GetSelectedAnnotation()?.Kind == AnnotationKind.Text)
            return AnnotationBoundsEditor.Nudge(originalSourceBounds, currentSourcePoint.X - dragStartSourcePoint.X,
                currentSourcePoint.Y - dragStartSourcePoint.Y, sourceSize);

        return handle == AnnotationBoundsHandle.Move
            ? AnnotationBoundsEditor.Move(
                originalSourceBounds,
                currentSourcePoint.X - dragStartSourcePoint.X,
                currentSourcePoint.Y - dragStartSourcePoint.Y,
                sourceSize)
            : AnnotationBoundsEditor.Resize(
                originalSourceBounds,
                handle,
                currentSourcePoint,
                sourceSize);
    }

    public async Task UpdateAnnotationBoundsAsync(
        Guid annotationId,
        RectD sourceBounds,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null)
        {
            return;
        }

        AnnotationObject? annotation = _currentDocument.Annotations
            .FirstOrDefault(candidate => candidate.Id == annotationId);
        if (annotation is null)
        {
            _annotationSelection.Clear();
            RefreshAnnotationOverlays();
            NotifyEditorStateChanged();
            return;
        }

        RectD before = annotation.Bounds;
        RectD after = annotation.Kind == AnnotationKind.Text ? Normalize(sourceBounds) : NormalizeAndClampSourceBounds(
            _currentDocument,
            sourceBounds,
            annotation.Kind);
        if (BoundsAreEquivalent(before, after))
        {
            return;
        }

        if (annotation.Kind == AnnotationKind.Text && TextSizeChanged(before, after))
        {
            (RectD bounds, AnnotationStyle style) = TextAnnotationLayout.Resize(annotation, after);
            await _annotationEditor.SetStyleAsync(_currentDocument, annotation, style, displayName, cancellationToken, bounds).ConfigureAwait(true);
            SyncStyleControlsFromSelectedAnnotation();
        }
        else
            await _annotationEditor.SetBoundsAsync(_currentDocument, annotation, after, displayName, cancellationToken).ConfigureAwait(true);

        if (!SelectedAnnotationIds.Contains(annotationId)) _annotationSelection.Select(_currentDocument, annotationId);
        // Commit the visual position before saving so release never shows the old bounds.
        RefreshAnnotationOverlays();
        NotifyEditorStateChanged();
        await SaveCurrentDocumentAsync($"{displayName} complete.", cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task AddAnnotationCoreAsync(
        AnnotationKind annotationKind,
        RectD? sourceBounds,
        CancellationToken cancellationToken,
        bool bypassPerspectiveSnapping = false)
    {
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null)
        {
            ShowCaptureNotice(
                "No document",
                "Open or capture an image before editing.",
                InfoBarSeverity.Warning);
            return;
        }

        AnnotationObject annotation = sourceBounds is RectD bounds
            ? CreateAnnotation(_currentDocument, annotationKind, bounds)
            : CreateCenteredAnnotation(_currentDocument, annotationKind);
        if (sourceBounds is null || annotationKind == AnnotationKind.Text) SnapNewObjectPlacement(annotation, bypassPerspectiveSnapping);

        if (!_layerWorkspace.TryAssignActiveLayer(_currentDocument, annotation))
        {
            StatusText = "Show the selected layer before adding an object.";
            return;
        }

        await _annotationEditor.AddAsync(
            _currentDocument,
            annotation,
            cancellationToken).ConfigureAwait(true);

        _annotationSelection.Select(_currentDocument, annotation.Id);
        await SaveCurrentDocumentAsync($"{annotationKind} added.", cancellationToken)
            .ConfigureAwait(true);
    }

    public void SelectAnnotation(Guid annotationId)
    {
        if (_currentDocument is null)
        {
            return;
        }

        if (_annotationSelection.Select(_currentDocument, annotationId))
        {
            _layerWorkspace.SelectAnnotation(GetSelectedAnnotation());
            SyncStyleControlsFromSelectedAnnotation();
            RefreshAnnotationOverlays();
            NotifyEditorStateChanged();
            StatusText = "Annotation selected.";
        }
    }

    public void ClearAnnotationSelection()
    {
        _pixelSelectionRequestVersion++;
        if (!HasCanvasSelection)
        {
            return;
        }

        _annotationSelection.Clear();
        ApplyAnnotationToolDefaultsWhenUnselected();
        RefreshAnnotationOverlays();
        NotifyEditorStateChanged();
        NotifyPropertyContextChanged();
        StatusText = "Selection cleared.";
    }

    public async Task DeleteSelectedAnnotationAsync(CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1)
        {
            await EditSelectionAsync((editor, document, ids) => editor.RemoveAsync(document, ids, cancellationToken), "Objects deleted.", cancellationToken, clearSelection: true);
            return;
        }
        if (_currentDocument is null
            || _documentRepository is null
            || _annotationEditor is null
            || _annotationSelection.SelectedAnnotationId is not Guid selectedId)
        {
            return;
        }

        AnnotationObject? annotation = _currentDocument.Annotations
            .FirstOrDefault(candidate => candidate.Id == selectedId);
        if (annotation is null)
        {
            _annotationSelection.Clear();
            RefreshAnnotationOverlays();
            NotifyEditorStateChanged();
            return;
        }

        await _annotationEditor.RemoveAsync(
            _currentDocument,
            annotation,
            cancellationToken).ConfigureAwait(true);

        _annotationSelection.Clear();
        await SaveCurrentDocumentAsync("Annotation deleted.", cancellationToken).ConfigureAwait(true);
    }

    public async Task DuplicateSelectedAnnotationAsync(CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1)
        {
            if (_currentDocument is null || _editCommandStack is null || _documentRepository is null) return;
            var copies = await new DocumentSelectionEditor(_editCommandStack).DuplicateAsync(_currentDocument, SelectedAnnotationIds, cancellationToken);
            _annotationSelection.SelectMany(_currentDocument, copies.Select(item => item.Id));
            RefreshSelection();
            await SaveCurrentDocumentAsync("Objects duplicated.", cancellationToken);
            return;
        }
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null
            || GetSelectedAnnotation() is not AnnotationObject annotation) return;
        AnnotationObject copy = await _annotationEditor.DuplicateAsync(_currentDocument, annotation, cancellationToken).ConfigureAwait(true);
        SelectAnnotation(copy.Id);
        await SaveCurrentDocumentAsync("Object duplicated.", cancellationToken).ConfigureAwait(true);
    }

    public async Task NudgeSelectedAnnotationAsync(double deltaX, double deltaY, CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1)
        {
            await EditSelectionAsync((editor, document, ids) => editor.NudgeAsync(document, ids, deltaX, deltaY, cancellationToken), "Objects moved.", cancellationToken);
            return;
        }
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null
            || GetSelectedAnnotation() is not AnnotationObject annotation) return;
        if (await _annotationEditor.NudgeAsync(_currentDocument, annotation, deltaX, deltaY, cancellationToken).ConfigureAwait(true))
        {
            RefreshAnnotationOverlays();
            await SaveCurrentDocumentAsync("Object moved.", cancellationToken).ConfigureAwait(true);
        }
    }

    public async Task ArrangeSelectedAnnotationAsync(bool bringToFront, CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1)
        {
            await ArrangeSelectionAsync(bringToFront ? 1 : -1, true, cancellationToken);
            return;
        }
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null
            || GetSelectedAnnotation() is not AnnotationObject annotation) return;
        if (await _annotationEditor.ArrangeInLayerAsync(_currentDocument, annotation, bringToFront, cancellationToken).ConfigureAwait(true))
            await SaveCurrentDocumentAsync(bringToFront ? "Object brought to front of layer." : "Object sent to back of layer.", cancellationToken).ConfigureAwait(true);
    }

    public void SelectLayer(Guid layerId)
    {
        if (!_layerWorkspace.SelectLayer(_currentDocument, layerId))
        {
            return;
        }

        _annotationSelection.Clear();
        ApplyAnnotationToolDefaultsWhenUnselected();
        RefreshAnnotationOverlays();
        NotifyEditorStateChanged();
        StatusText = "Layer selected. New objects will be added here.";
    }

    public void SetLayerExpanded(Guid layerId, bool isExpanded)
    {
        _layerWorkspace.SetExpanded(_currentDocument, layerId, isExpanded);
    }

    public bool TryGetActiveLayerForRemoval(
        out Guid layerId,
        out string layerName,
        out int objectCount)
    {
        layerId = Guid.Empty;
        layerName = string.Empty;
        objectCount = 0;
        if (_layerWorkspace.ActiveLayerId is not Guid activeLayerId
            || !TryGetLayerForRemoval(activeLayerId, out layerName, out objectCount))
        {
            return false;
        }

        layerId = activeLayerId;
        return true;
    }

    public bool TryGetLayerForRemoval(Guid layerId, out string layerName, out int objectCount)
    {
        layerName = string.Empty;
        objectCount = 0;
        if (_currentDocument is not { Layers.Count: > 1 })
        {
            return false;
        }

        AnnotationLayer? layer = _currentDocument.Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null)
        {
            return false;
        }

        layerName = layer.Name;
        objectCount = _currentDocument.Annotations.Count(annotation => annotation.LayerId == layer.Id);
        return true;
    }

    public async Task AddLayerAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        AnnotationLayer layer = await _layerWorkspace.AddLayerAsync(
            _currentDocument,
            _editCommandStack,
            cancellationToken).ConfigureAwait(true);
        _annotationSelection.Clear();
        await SaveCurrentDocumentAsync($"{layer.Name} added.", cancellationToken).ConfigureAwait(true);
    }

    public async Task RenameLayerAsync(Guid layerId, string name, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        bool changed = await _layerWorkspace.RenameLayerAsync(
            _currentDocument, layerId, name, _editCommandStack, cancellationToken).ConfigureAwait(true);
        await CompleteLayerChangeAsync(changed, "Layer renamed.", cancellationToken).ConfigureAwait(true);
    }

    public async Task MoveLayerAsync(Guid layerId, int direction, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        bool changed = await _layerWorkspace.MoveLayerAsync(
            _currentDocument, layerId, direction, _editCommandStack, cancellationToken).ConfigureAwait(true);
        await CompleteLayerChangeAsync(changed, direction > 0 ? "Layer moved up." : "Layer moved down.", cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task SetLayerColorAsync(Guid layerId, LayerColorLabel color, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        bool changed = await _layerWorkspace.SetLayerColorAsync(
            _currentDocument, layerId, color, _editCommandStack, cancellationToken).ConfigureAwait(true);
        await CompleteLayerChangeAsync(changed, "Layer color updated.", cancellationToken).ConfigureAwait(true);
    }

    public async Task SetLayerCustomColorAsync(Guid layerId, ColorRgba color, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        bool changed = await _layerWorkspace.SetLayerCustomColorAsync(
            _currentDocument, layerId, color, _editCommandStack, cancellationToken).ConfigureAwait(true);
        await CompleteLayerChangeAsync(changed, "Custom layer color updated.", cancellationToken).ConfigureAwait(true);
    }

    private async Task CompleteLayerChangeAsync(bool changed, string status, CancellationToken cancellationToken)
    {
        if (!changed)
        {
            return;
        }

        _annotationSelection.Clear();
        ApplyAnnotationToolDefaultsWhenUnselected();
        await SaveCurrentDocumentAsync(status, cancellationToken).ConfigureAwait(true);
    }

    public async Task DeleteLayerAsync(Guid layerId, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        int objectCount = _currentDocument.Annotations.Count(annotation => annotation.LayerId == layerId);
        bool hadSelection = _annotationSelection.HasSelection;
        bool removed = await _layerWorkspace.RemoveLayerAsync(
            _currentDocument,
            layerId,
            _editCommandStack,
            cancellationToken).ConfigureAwait(true);
        if (!removed)
        {
            return;
        }

        _annotationSelection.RetainExisting(_currentDocument);
        if (hadSelection && !_annotationSelection.HasSelection)
        {
            ApplyAnnotationToolDefaultsWhenUnselected();
        }

        string status = objectCount == 0
            ? "Layer deleted."
            : $"Layer and {objectCount} object{(objectCount == 1 ? string.Empty : "s")} deleted.";
        await SaveCurrentDocumentAsync(status, cancellationToken).ConfigureAwait(true);
    }

    public async Task MoveAnnotationInLayerAsync(Guid annotationId, int direction, CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1 && SelectedAnnotationIds.Contains(annotationId))
        {
            await ArrangeSelectionAsync(direction, false, cancellationToken);
            return;
        }
        if (_currentDocument is null || _documentRepository is null || _annotationEditor is null)
        {
            return;
        }

        AnnotationObject? annotation = _currentDocument.Annotations.FirstOrDefault(candidate => candidate.Id == annotationId);
        if (annotation is null || !await _annotationEditor.MoveInLayerAsync(_currentDocument, annotation, direction, cancellationToken)
            .ConfigureAwait(true))
        {
            return;
        }

        if (!SelectedAnnotationIds.Contains(annotationId)) _annotationSelection.Select(_currentDocument, annotationId);
        _layerWorkspace.SelectAnnotation(annotation);
        SyncStyleControlsFromSelectedAnnotation();
        await SaveCurrentDocumentAsync(direction > 0 ? "Object moved up." : "Object moved down.", cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task MoveSelectedAnnotationToLayerAsync(
        Guid layerId,
        CancellationToken cancellationToken)
    {
        if (SelectedAnnotationCount > 1)
        {
            await EditSelectionAsync((editor, document, ids) => editor.MoveToLayerAsync(document, ids, layerId, cancellationToken), "Objects moved to layer.", cancellationToken);
            return;
        }
        if (_currentDocument is null
            || _documentRepository is null
            || _editCommandStack is null
            || !_annotationSelection.HasSelection)
        {
            return;
        }

        AnnotationLayer? layer = await _layerWorkspace.MoveAnnotationAsync(
            _currentDocument,
            GetSelectedAnnotation(),
            layerId,
            _editCommandStack,
            cancellationToken).ConfigureAwait(true);
        if (layer is null)
        {
            return;
        }

        await SaveCurrentDocumentAsync($"Object moved to {layer.Name}.", cancellationToken).ConfigureAwait(true);
    }

    public async Task ToggleLayerVisibilityAsync(Guid layerId, CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null || _editCommandStack is null)
        {
            return;
        }

        bool? nextVisibility = await _layerWorkspace.ToggleVisibilityAsync(
            _currentDocument,
            layerId,
            _editCommandStack,
            cancellationToken).ConfigureAwait(true);
        if (nextVisibility is null)
        {
            return;
        }

        if (nextVisibility == false)
        {
            _annotationSelection.SelectMany(_currentDocument, _currentDocument.Annotations
                .Where(annotation => SelectedAnnotationIds.Contains(annotation.Id) && annotation.LayerId != layerId).Select(annotation => annotation.Id));
        }

        await SaveCurrentDocumentAsync(
            nextVisibility == true ? "Layer shown." : "Layer hidden.",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task ToggleAnnotationVisibilityAsync(
        Guid annotationId,
        CancellationToken cancellationToken)
    {
        if (_currentDocument is null
            || _documentRepository is null
            || _annotationEditor is null)
        {
            return;
        }

        AnnotationObject? annotation = _currentDocument.Annotations
            .FirstOrDefault(candidate => candidate.Id == annotationId);
        if (annotation is null)
        {
            return;
        }

        bool nextVisibility = !annotation.IsVisible;
        await _annotationEditor.SetVisibilityAsync(
            _currentDocument,
            annotation,
            nextVisibility,
            cancellationToken).ConfigureAwait(true);
        await SaveCurrentDocumentAsync(
                nextVisibility ? "Object shown." : "Object hidden.",
                cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task CropToSelectedAnnotationAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null
            || _documentRepository is null
            || _editCommandStack is null
            || _documentRasterEditor is null
            || SourceSelectionBounds is not RectD cropBounds)
        {
            return;
        }

        if (cropBounds.Width < 1 || cropBounds.Height < 1)
        {
            StatusText = "Crop selection is too small.";
            return;
        }

        RasterEditResult rasterResult = await _documentRasterEditor
            .CropAsync(
                new RasterCropRequest(
                    _currentDocument.SourceImage,
                    cropBounds,
                    ResolveRasterOutputDirectory(_currentDocument)),
                cancellationToken)
            .ConfigureAwait(true);

        if (!rasterResult.Succeeded || rasterResult.Image is not ImageAsset nextImage)
        {
            StatusText = $"Crop failed: {rasterResult.ErrorMessage}";
            return;
        }

        List<AnnotationObject> beforeAnnotations = CloneAnnotations(_currentDocument.Annotations);
        List<DestructiveEditOperation> beforeOperations = [.. _currentDocument.DestructiveOperations];
        List<AnnotationObject> afterAnnotations = TransformAnnotationsForCrop(
            _currentDocument.Annotations,
            cropBounds);
        List<DestructiveEditOperation> afterOperations =
        [
            ..beforeOperations,
            new DestructiveEditOperation(
                Guid.NewGuid(),
                DestructiveOperationKind.Crop,
                cropBounds,
                new Dictionary<string, string>())
        ];

        await _editCommandStack
            .ExecuteAsync(
                _currentDocument,
                new UpdateDocumentRasterCommand(
                    _currentDocument.SourceImage,
                    nextImage,
                    beforeAnnotations,
                    afterAnnotations,
                    beforeOperations,
                    afterOperations,
                    "Crop Document"),
                cancellationToken)
            .ConfigureAwait(true);

        _annotationSelection.Clear();
        RefreshCanvasAfterRasterEdit();
        await SaveCurrentDocumentAsync("Crop complete.", cancellationToken).ConfigureAwait(true);
        await InvalidateOcrCacheAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task ResizeCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null
            || _documentRepository is null
            || _editCommandStack is null
            || _documentRasterEditor is null)
        {
            return;
        }

        ResizeDimensions targetDimensions = CreateTargetResizeDimensions();
        int width = Math.Max(1, (int)Math.Round(targetDimensions.Width));
        int height = Math.Max(1, (int)Math.Round(targetDimensions.Height));
        if (width == _currentDocument.SourceImage.Width
            && height == _currentDocument.SourceImage.Height)
        {
            StatusText = "Resize dimensions are unchanged.";
            return;
        }

        RasterEditResult rasterResult = await _documentRasterEditor
            .ResizeAsync(
                new RasterResizeRequest(
                    _currentDocument.SourceImage,
                    width,
                    height,
                    ResolveRasterOutputDirectory(_currentDocument)),
                cancellationToken)
            .ConfigureAwait(true);

        if (!rasterResult.Succeeded || rasterResult.Image is not ImageAsset nextImage)
        {
            StatusText = $"Resize failed: {rasterResult.ErrorMessage}";
            return;
        }

        List<AnnotationObject> beforeAnnotations = CloneAnnotations(_currentDocument.Annotations);
        List<DestructiveEditOperation> beforeOperations = [.. _currentDocument.DestructiveOperations];
        double scaleX = (double)width / Math.Max(1, _currentDocument.SourceImage.Width);
        double scaleY = (double)height / Math.Max(1, _currentDocument.SourceImage.Height);
        List<AnnotationObject> afterAnnotations = TransformAnnotationsForResize(
            _currentDocument.Annotations,
            scaleX,
            scaleY);
        List<DestructiveEditOperation> afterOperations =
        [
            ..beforeOperations,
            new DestructiveEditOperation(
                Guid.NewGuid(),
                DestructiveOperationKind.Resize,
                null,
                new Dictionary<string, string>
                {
                    ["width"] = width.ToString(CultureInfo.InvariantCulture),
                    ["height"] = height.ToString(CultureInfo.InvariantCulture)
                })
        ];

        await _editCommandStack
            .ExecuteAsync(
                _currentDocument,
                new UpdateDocumentRasterCommand(
                    _currentDocument.SourceImage,
                    nextImage,
                    beforeAnnotations,
                    afterAnnotations,
                    beforeOperations,
                    afterOperations,
                    "Resize Document"),
                cancellationToken)
            .ConfigureAwait(true);

        _annotationSelection.Clear();
        RefreshCanvasAfterRasterEdit();
        await SaveCurrentDocumentAsync("Resize complete.", cancellationToken).ConfigureAwait(true);
        await InvalidateOcrCacheAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task UndoAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _editCommandStack is null)
        {
            return;
        }

        ImageAsset beforeImage = _currentDocument.SourceImage;
        bool changed = await _editCommandStack
            .UndoAsync(_currentDocument, cancellationToken)
            .ConfigureAwait(true);

        if (changed)
        {
            bool sourceImageChanged = beforeImage != _currentDocument.SourceImage;
            _annotationSelection.RetainExisting(_currentDocument);
            _layerWorkspace.RetainActiveLayer(_currentDocument, GetSelectedAnnotation());
            SyncStyleControlsFromSelectedAnnotation();
            if (sourceImageChanged) RefreshCanvasAfterRasterEdit();
            await SaveCurrentDocumentAsync("Undo complete.", cancellationToken).ConfigureAwait(true);
            if (sourceImageChanged)
            {
                await InvalidateOcrCacheAsync(cancellationToken).ConfigureAwait(true);
            }
        }
    }

    public async Task RedoAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _editCommandStack is null)
        {
            return;
        }

        ImageAsset beforeImage = _currentDocument.SourceImage;
        bool changed = await _editCommandStack
            .RedoAsync(_currentDocument, cancellationToken)
            .ConfigureAwait(true);

        if (changed)
        {
            bool sourceImageChanged = beforeImage != _currentDocument.SourceImage;
            _annotationSelection.RetainExisting(_currentDocument);
            _layerWorkspace.RetainActiveLayer(_currentDocument, GetSelectedAnnotation());
            SyncStyleControlsFromSelectedAnnotation();
            if (sourceImageChanged) RefreshCanvasAfterRasterEdit();
            await SaveCurrentDocumentAsync("Redo complete.", cancellationToken).ConfigureAwait(true);
            if (sourceImageChanged)
            {
                await InvalidateOcrCacheAsync(cancellationToken).ConfigureAwait(true);
            }
        }
    }

    public async Task UpdateCaptureHotkeyAsync(
        int hotkeyIndex,
        CancellationToken cancellationToken)
    {
        if (_settingsStore is null || _hotkeyService is null)
        {
            return;
        }

        if (hotkeyIndex < 0 || hotkeyIndex >= CaptureHotkeyOptions.Length)
        {
            return;
        }

        if (hotkeyIndex == CaptureHotkeyIndex)
        {
            return;
        }

        string previousGesture = CaptureHotkeyOptions[CaptureHotkeyIndex];
        string nextGesture = CaptureHotkeyOptions[hotkeyIndex];
        ApplicationSettings settings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true);

        await _hotkeyService.UnregisterAsync("Capture", cancellationToken).ConfigureAwait(true);
        HotkeyRegistrationResult result = await _hotkeyService
            .RegisterAsync(new HotkeyRegistration("Capture", nextGesture), cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            CaptureHotkeyIndex = hotkeyIndex;
            await _settingsStore
                .SaveAsync(settings with { CaptureHotkey = nextGesture }, cancellationToken)
                .ConfigureAwait(true);

            StatusText = $"Capture hotkey: {nextGesture}.";
            ShowCaptureNotice(
                "Hotkey updated",
                $"Capture hotkey is now {nextGesture}.",
                InfoBarSeverity.Success);
            return;
        }

        await _hotkeyService
            .RegisterAsync(new HotkeyRegistration("Capture", previousGesture), cancellationToken)
            .ConfigureAwait(true);
        OnPropertyChanged(nameof(CaptureHotkeyIndex));

        string errorMessage = result.ErrorMessage ?? "The capture hotkey could not be registered.";
        StatusText = $"Capture hotkey unavailable: {errorMessage}";
        ShowCaptureNotice(
            "Hotkey unavailable",
            errorMessage,
            InfoBarSeverity.Warning);
    }

    public void StartEditorMessageLoop()
    {
        if (_editorMessageServer is null || _messageLoopTask is not null)
        {
            return;
        }

        _messageLoopCancellation = new CancellationTokenSource();
        _messageLoopTask = Task.Run(() => RunEditorMessageLoopAsync(_messageLoopCancellation.Token));
    }

    public Task HandleAsync(EditorMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return HandleCoreAsync(message, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageLoopCancellation?.Cancel();
        _messageLoopCancellation?.Dispose();
        _recentThumbnailRefreshCancellation.Cancel();
        _recentThumbnailRefreshCancellation.Dispose();

        if (_hotkeyService is not null)
        {
            _hotkeyService.HotkeyPressed -= HotkeyService_HotkeyPressed;
            if (_hotkeyService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task RunEditorMessageLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _editorMessageServer!
                .RunAsync(this, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetStatus($"Editor message listener stopped: {exception.Message}");
            if (_diagnosticLog is not null)
            {
                await _diagnosticLog
                    .WriteAsync(
                        DiagnosticEvent.Create(
                            DiagnosticSeverity.Error,
                            "SnapStudio.App.Ipc",
                            "Editor message listener stopped.",
                            new Dictionary<string, string>
                            {
                                ["exceptionType"] = exception.GetType().Name,
                                ["message"] = exception.Message
                            }),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task HandleCoreAsync(EditorMessage message, CancellationToken cancellationToken)
    {
        string status = message switch
        {
            CaptureCompletedEditorMessage completed => $"Capture received: {completed.DocumentId}",
            CaptureFailedEditorMessage failed => $"Capture failed: {failed.Message}",
            PingEditorMessage ping => $"IPC ping received from {ping.Sender}.",
            _ => $"Message received: {message.MessageType}"
        };

        SetStatus(status);

        if (message is CaptureCompletedEditorMessage completedMessage)
        {
            await RunOnDispatcherAsync(
                    async () =>
                    {
                        await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
                        await OpenDocumentAsync(completedMessage.DocumentId, cancellationToken).ConfigureAwait(true);
                    })
                .ConfigureAwait(false);
        }

        await WriteMessageDiagnosticAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private Task RunOnDispatcherAsync(Func<Task> action)
    {
        if (_dispatcherQueue is null)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = _dispatcherQueue.TryEnqueue(
            async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });

        if (!enqueued)
        {
            completion.SetResult();
        }

        return completion.Task;
    }

    private Task WriteMessageDiagnosticAsync(EditorMessage message, CancellationToken cancellationToken)
    {
        if (_diagnosticLog is null)
        {
            return Task.CompletedTask;
        }

        return _diagnosticLog.WriteAsync(
            DiagnosticEvent.Create(
                DiagnosticSeverity.Information,
                "SnapStudio.App.Ipc",
                "Editor message received.",
                new Dictionary<string, string>
                {
                    ["messageType"] = message.MessageType,
                    ["protocolVersion"] = message.ProtocolVersion.ToString()
                }),
            cancellationToken);
    }

    private async Task RegisterCaptureHotkeyAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        if (_hotkeyService is null || string.IsNullOrWhiteSpace(settings.CaptureHotkey))
        {
            return;
        }

        HotkeyRegistrationResult result = await _hotkeyService
            .RegisterAsync(
                new HotkeyRegistration("Capture", settings.CaptureHotkey),
                cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            StatusText = $"Ready. Capture hotkey: {settings.CaptureHotkey}.";
            return;
        }

        string errorMessage = result.ErrorMessage ?? "The capture hotkey could not be registered.";
        StatusText = $"Capture hotkey unavailable: {errorMessage}";
        ShowCaptureNotice(
            "Hotkey unavailable",
            errorMessage,
            InfoBarSeverity.Warning);
    }

    private static int ResolveCaptureHotkeyIndex(string captureHotkey)
    {
        int index = Array.FindIndex(
            CaptureHotkeyOptions,
            option => string.Equals(option, captureHotkey, StringComparison.OrdinalIgnoreCase));

        return index >= 0 ? index : 0;
    }

    private static int ResolveStorageBackendIndex(ApplicationStorageBackend storageBackend)
    {
        return storageBackend == ApplicationStorageBackend.Database
            ? StorageBackendDatabaseIndex
            : StorageBackendFileSystemIndex;
    }

    private static ApplicationStorageBackend ResolveStorageBackend(int storageBackendIndex)
    {
        return storageBackendIndex == StorageBackendDatabaseIndex
            ? ApplicationStorageBackend.Database
            : ApplicationStorageBackend.FileSystem;
    }

    private void HotkeyService_HotkeyPressed(object? sender, HotkeyPressedEvent e)
    {
        if (!string.Equals(e.Name, "Capture", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_dispatcherQueue is null)
        {
            _ = CaptureAsync(CaptureMode.Picker, CancellationToken.None);
            return;
        }

        bool enqueued = _dispatcherQueue.TryEnqueue(
            () => _ = CaptureAsync(CaptureMode.Picker, CancellationToken.None));

        if (!enqueued)
        {
            StatusText = "Capture hotkey could not be handled.";
        }
    }

    private async Task RefreshRecentDocumentsAsync(CancellationToken cancellationToken)
    {
        if (_documentCatalog is null)
        {
            return;
        }

        IReadOnlyList<DocumentSummary> summaries = await _documentCatalog
            .GetRecentAsync(int.MaxValue, cancellationToken)
            .ConfigureAwait(true);

        // Keep only metadata for the full history; create image controls and thumbnails for the visible page.
        var previousIds = _allRecentDocuments.Select(document => document.Id).ToHashSet();
        DocumentSummary[] added = summaries.Where(summary => !previousIds.Contains(summary.Id)).ToArray();
        if (added.Length > 0)
        {
            _historyPagination.Reset();
            // A new unfiled capture must remain visible when a named folder is selected.
            if (!_historyFolderOperationInProgress && added.Any(summary => !MatchesHistoryFolder(summary)))
            {
                _selectedHistoryFolderId = null;
                _historyUnfiledOnly = false;
            }
        }
        _allRecentDocuments.Clear();
        _allRecentDocuments.AddRange(summaries);
        RefreshHistoryFolderItems();
        ApplyRecentDocumentFilter(refreshThumbnails: true);
    }

    private void QueueRecentThumbnailRefresh(IReadOnlyList<DocumentSummary> summaries)
    {
        if (_documentThumbnailCache is null || _disposed)
        {
            return;
        }

        // A newer list supersedes pending work, not an in-flight cache render.
        // Only disposal cancels the token passed through the cache to the renderer.
        _ = RefreshRecentDocumentThumbnailsAsync(
            summaries.ToArray(),
            _recentThumbnailRefreshCancellation.Token);
    }

    private async Task RefreshRecentDocumentThumbnailsAsync(
        IReadOnlyList<DocumentSummary> summaries,
        CancellationToken cancellationToken)
    {
        try
        {
            await _recentThumbnailRefresh
                .RefreshAsync(
                    summaries,
                    ResolveThumbnailPathAsync,
                    UpdateRecentDocumentThumbnail,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.IsCancellationRequested
            && exception.CancellationToken == cancellationToken)
        {
            // Expected shutdown; superseding a refresh never cancels this token.
        }
        catch (Exception exception)
        {
            SetStatus($"Thumbnail refresh failed: {exception.Message}");
            await LogThumbnailFailureAsync(null, exception.Message, exception.GetType().Name)
                .ConfigureAwait(true);
        }
    }

    private async Task<string?> ResolveThumbnailPathAsync(
        DocumentSummary summary,
        CancellationToken cancellationToken)
    {
        if (_documentThumbnailCache is null)
        {
            return summary.ThumbnailPath;
        }

        DocumentThumbnailResult result;
        try
        {
            result = await _documentThumbnailCache
                .EnsureThumbnailAsync(
                    new DocumentThumbnailRequest(
                        summary.Id,
                        summary.ModifiedAtUtc,
                        ThumbnailMaximumPixelSize),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            await LogThumbnailFailureAsync(summary.Id, exception.Message, exception.GetType().Name)
                .ConfigureAwait(true);
            return summary.ThumbnailPath;
        }

        if (!result.Succeeded)
        {
            await LogThumbnailFailureAsync(
                    summary.Id,
                    result.ErrorMessage ?? "The thumbnail could not be generated.")
                .ConfigureAwait(true);
        }

        return result.Succeeded
            ? result.ThumbnailPath
            : summary.ThumbnailPath;
    }

    private Task LogThumbnailFailureAsync(
        DocumentId? documentId,
        string message,
        string? exceptionType = null)
    {
        if (_diagnosticLog is null)
        {
            return Task.CompletedTask;
        }

        return _diagnosticLog.WriteAsync(
            DiagnosticEvent.Create(
                DiagnosticSeverity.Warning,
                "SnapStudio.App.Thumbnails",
                "Thumbnail refresh failed.",
                new Dictionary<string, string>
                {
                    ["documentId"] = documentId?.ToString() ?? string.Empty,
                    ["error"] = message,
                    ["exceptionType"] = exceptionType ?? string.Empty
                }),
            CancellationToken.None);
    }

    private void UpdateRecentDocumentThumbnail(
        DocumentSummary summary,
        string thumbnailPath)
    {
        int index = _allRecentDocuments.FindIndex(
            document => document.Id == summary.Id);
        if (index < 0 || _allRecentDocuments[index].ModifiedAtUtc != summary.ModifiedAtUtc
            || string.Equals(
                _allRecentDocuments[index].ThumbnailPath,
                thumbnailPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _allRecentDocuments[index] = _allRecentDocuments[index] with { ThumbnailPath = thumbnailPath };
        for (int pageIndex = 0; pageIndex < RecentDocuments.Count; pageIndex++)
        {
            if (RecentDocuments[pageIndex].Id == summary.Id.ToString())
            {
                RecentDocuments[pageIndex].UpdateFromSummary(_allRecentDocuments[index]);
                break;
            }
        }
    }

    private void RemoveRecentDocument(DocumentId documentId)
    {
        int removedCount = _allRecentDocuments.RemoveAll(
            document => document.Id == documentId);
        if (removedCount > 0)
        {
            ApplyRecentDocumentFilter();
        }
    }

    private void ApplyRecentDocumentFilter(bool refreshThumbnails = false)
    {
        IEnumerable<DocumentSummary> documents = _allRecentDocuments.Where(MatchesHistoryFolder).Where(MatchesHistoryFilter);
        if (!string.IsNullOrWhiteSpace(_historySearchText))
        {
            documents = documents.Where(MatchesHistorySearch);
        }

        DocumentSummary[] matches = documents.ToArray();
        _historyPagination.UpdateCount(matches.Length);
        DocumentSummary[] page = matches.Skip(_historyPagination.Offset).Take(HistoryPageSize).ToArray();
        var existing = RecentDocuments.ToDictionary(item => item.Id);
        var items = new List<DocumentSummaryItem>();
        foreach (DocumentSummary document in page)
        {
            if (existing.TryGetValue(document.Id.ToString(), out DocumentSummaryItem? item)) item.UpdateFromSummary(document);
            else item = DocumentSummaryItem.FromSummary(document);
            items.Add(item);
        }
        CollectionUpdates.Synchronize(RecentDocuments, items);
        NotifyHistoryPaginationChanged();
        if (refreshThumbnails) QueueRecentThumbnailRefresh(page);
    }

    private bool MatchesHistoryFilter(DocumentSummary document)
    {
        string sourceKind = DocumentSummaryItem.NormalizeSourceKind(document.SourceKind);
        return HistoryFilter switch
        {
            DocumentHistoryFilter.Captures => string.Equals(
                sourceKind,
                "capture",
                StringComparison.OrdinalIgnoreCase),
            DocumentHistoryFilter.Imports => string.Equals(
                sourceKind,
                "import",
                StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceKind, "clipboard", StringComparison.OrdinalIgnoreCase),
            DocumentHistoryFilter.Scrolling => string.Equals(
                sourceKind,
                "scrollingcapture",
                StringComparison.OrdinalIgnoreCase),
            DocumentHistoryFilter.Duplicates => string.Equals(
                sourceKind,
                "duplicate",
                StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private bool MatchesHistorySearch(DocumentSummary document) => DocumentSummaryItem.MatchesSearch(document, _historySearchText);

    private static IReadOnlyCollection<CaptureTargetKind> ResolveAllowedTargets(CaptureMode captureMode)
    {
        return captureMode switch
        {
            CaptureMode.Region => [CaptureTargetKind.Region],
            CaptureMode.Display => [CaptureTargetKind.Display],
            CaptureMode.FullScreen => [CaptureTargetKind.FullScreen],
            CaptureMode.Window => [CaptureTargetKind.Window],
            _ => [CaptureTargetKind.Display, CaptureTargetKind.Window]
        };
    }

    private static CaptureTargetSelectionMode ResolveSelectionMode(CaptureMode captureMode)
    {
        return captureMode == CaptureMode.Picker
            ? CaptureTargetSelectionMode.Picker
            : CaptureTargetSelectionMode.Automatic;
    }

    private static string CreateCapturePrompt(CaptureMode captureMode, TimeSpan delay)
    {
        string prompt = captureMode switch
        {
            CaptureMode.Region => "Select a region to capture.",
            CaptureMode.Display => "Choose a display to capture.",
            CaptureMode.FullScreen => "Capturing all connected displays.",
            CaptureMode.Window => "Choose a window to capture.",
            _ => "Choose a window or display to capture."
        };

        return delay > TimeSpan.Zero
            ? $"{prompt} Capture starts {delay.TotalSeconds:0} seconds after selection."
            : prompt;
    }

    private static string CreateCaptureRecoveryMessage(CaptureFailure? failure)
    {
        return failure?.Reason switch
        {
            CaptureFailureReason.PermissionDenied =>
                "Windows denied capture access. Check screen-capture permissions and try again.",
            CaptureFailureReason.TargetUnavailable =>
                failure.Message,
            CaptureFailureReason.Unsupported =>
                failure.Message,
            _ => "Try another window, display, or region. If the target is protected or elevated, Windows may block capture."
        };
    }

    private static string CreateScreenRecordingFailureMessage(ScreenRecordingFailure? failure)
    {
        return failure?.Reason switch
        {
            ScreenRecordingFailureReason.PermissionDenied =>
                "Windows denied screen recording access. Check screen-capture permissions and try again.",
            ScreenRecordingFailureReason.TargetUnavailable =>
                "No recording target was available. Choose another window or display.",
            ScreenRecordingFailureReason.AlreadyRecording =>
                "A recording is already active. Stop it before starting another recording.",
            ScreenRecordingFailureReason.Unsupported
                or ScreenRecordingFailureReason.EncoderUnavailable
                or ScreenRecordingFailureReason.AudioUnavailable
                or ScreenRecordingFailureReason.OutputUnavailable =>
                failure.Message,
            _ => failure?.Message ?? "Try another window or display. If the target is protected or elevated, Windows may block recording."
        };
    }

    private static string CreateSettingsTransferFailureMessage(SettingsTransferFailure? failure)
    {
        return failure?.Reason switch
        {
            SettingsTransferFailureReason.InvalidPath =>
                failure.Message,
            SettingsTransferFailureReason.InvalidJson =>
                "The selected file is not a valid SnapStudio settings file.",
            SettingsTransferFailureReason.PermissionDenied =>
                "Windows denied access to the selected settings file.",
            SettingsTransferFailureReason.FileUnavailable =>
                failure.Message,
            _ => failure?.Message ?? "The settings file could not be processed."
        };
    }

    private static string ResolvePrivacyDocumentPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Docs", PrivacyDocumentFileName);
    }

    private static ApplicationSettings NormalizeImportedSettings(ApplicationSettings settings)
    {
        int hotkeyIndex = ResolveCaptureHotkeyIndex(settings.CaptureHotkey);
        int storageBackendIndex = ResolveStorageBackendIndex(settings.StorageBackend);

        return settings with
        {
            CaptureHotkey = CaptureHotkeyOptions[hotkeyIndex],
            StorageBackend = ResolveStorageBackend(storageBackendIndex)
        };
    }

    private static bool FeatureFlagsDiffer(
        IReadOnlyDictionary<string, bool> currentFeatureFlags,
        IReadOnlyDictionary<string, bool> importedFeatureFlags)
    {
        if (currentFeatureFlags.Count != importedFeatureFlags.Count)
        {
            return true;
        }

        foreach ((string key, bool value) in currentFeatureFlags)
        {
            if (!importedFeatureFlags.TryGetValue(key, out bool importedValue)
                || importedValue != value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ResolveFeatureFlag(
        IReadOnlyDictionary<string, bool> featureFlags,
        string flagName,
        bool fallback)
    {
        return featureFlags.TryGetValue(flagName, out bool value)
            ? value
            : fallback;
    }

    private async Task<bool> SourceImageIsReferencedByAnotherDocumentAsync(
        DocumentId currentDocumentId,
        string sourceImagePath,
        CancellationToken cancellationToken)
    {
        if (_documentCatalog is null)
        {
            return false;
        }

        IReadOnlyList<DocumentSummary> summaries = await _documentCatalog
            .GetRecentAsync(int.MaxValue, cancellationToken)
            .ConfigureAwait(true);

        foreach (DocumentSummary summary in summaries)
        {
            if (summary.Id != currentDocumentId
                && PathsAreEquivalent(summary.SourceImagePath, sourceImagePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathsAreEquivalent(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private Dictionary<string, bool> CreateSettingsFeatureFlags(
        IReadOnlyDictionary<string, bool> existingFeatureFlags)
    {
        var updated = new Dictionary<string, bool>(existingFeatureFlags, StringComparer.Ordinal)
        {
            [StillCaptureFeatureFlag] = SettingsStillCaptureEnabled,
            [DelayedCaptureFeatureFlag] = SettingsDelayedCaptureEnabled,
            [IncludeCursorFeatureFlag] = SettingsCursorCaptureEnabled,
            [GdiFallbackFeatureFlag] = SettingsGdiFallbackEnabled,
            [BlurToolFeatureFlag] = SettingsBlurToolEnabled,
            [PdfExportFeatureFlag] = SettingsPdfExportEnabled,
            [PinToScreenFeatureFlag] = SettingsPinToScreenEnabled,
            [OcrFeatureFlag] = SettingsOcrEnabled,
            [ScrollingCaptureFeatureFlag] = SettingsScrollingCaptureEnabled,
            [ScreenRecordingFeatureFlag] = SettingsScreenRecordingEnabled,
            [SmartRedactFeatureFlag] = SettingsSmartRedactEnabled,
            [PluginSdkFeatureFlag] = SettingsPluginSdkEnabled
        };

        return updated;
    }

    private void InitializeSettingsFeatureFlags(ApplicationSettings settings)
    {
        SettingsStillCaptureEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            StillCaptureFeatureFlag,
            true);
        SettingsDelayedCaptureEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            DelayedCaptureFeatureFlag,
            true);
        SettingsCursorCaptureEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            IncludeCursorFeatureFlag,
            true);
        SettingsGdiFallbackEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            GdiFallbackFeatureFlag,
            false);
        SettingsBlurToolEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            BlurToolFeatureFlag,
            true);
        SettingsPdfExportEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            PdfExportFeatureFlag,
            true);
        SettingsPinToScreenEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            PinToScreenFeatureFlag,
            true);
        SettingsOcrEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            OcrFeatureFlag,
            false);
        SettingsScrollingCaptureEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            ScrollingCaptureFeatureFlag,
            false);
        SettingsScreenRecordingEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            ScreenRecordingFeatureFlag,
            false);
        SettingsSmartRedactEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            SmartRedactFeatureFlag,
            false);
        SettingsPluginSdkEnabled = ResolveFeatureFlag(
            settings.FeatureFlags,
            PluginSdkFeatureFlag,
            false);
    }

    private void ApplyImportedSettings(ApplicationSettings settings)
    {
        InitializeTheme(settings);
        CaptureHotkeyIndex = ResolveCaptureHotkeyIndex(settings.CaptureHotkey);
        IncludeCursor = settings.IncludeCursorByDefault;
        CopyCapturesToClipboard = settings.CopyCapturesToClipboard;
        FirstRunStorageRoot = settings.StorageRoot;
        FirstRunStorageBackendIndex = ResolveStorageBackendIndex(settings.StorageBackend);
        FirstRunCaptureHotkeyIndex = CaptureHotkeyIndex;
        FirstRunIncludeCursor = settings.IncludeCursorByDefault;
        FirstRunCopyCapturesToClipboard = settings.CopyCapturesToClipboard;
        InitializeSettingsFeatureFlags(settings);

        if (!settings.FirstRunCompleted)
        {
            SettingsSurfaceIsFirstRun = true;
            FirstRunSetupIsOpen = true;
        }
        else if (FirstRunSetupIsOpen)
        {
            SettingsSurfaceIsFirstRun = false;
        }
    }

    private static string CreateScrollingPartialMessage(ScrollingCaptureResult result)
    {
        string message = result.Failure?.Message
            ?? "The selected target stopped before scrolling capture completed.";

        return result.Diagnostics.TryGetValue("diagnosticsBundlePath", out string? bundlePath)
            && !string.IsNullOrWhiteSpace(bundlePath)
                ? $"{message} A partial image was saved. Diagnostics bundle: {bundlePath}"
                : $"{message} A partial image was saved.";
    }

    private static string CreateScrollingFailureMessage(ScrollingCaptureResult result)
    {
        string message = result.Failure?.Message
            ?? "Scrolling capture did not produce an image.";

        return result.Diagnostics.TryGetValue("diagnosticsBundlePath", out string? bundlePath)
            && !string.IsNullOrWhiteSpace(bundlePath)
                ? $"{message} Diagnostics bundle: {bundlePath}"
                : message;
    }

    private string CreateScrollingCaptureOutputDirectory()
    {
        string root = string.IsNullOrWhiteSpace(StorageRoot)
            ? Path.Combine(Path.GetTempPath(), "SnapStudio")
            : StorageRoot;

        return Path.Combine(root, "SourceImages", "ScrollingCaptures");
    }

    private string CreateScreenRecordingOutputPath()
    {
        string root = string.IsNullOrWhiteSpace(StorageRoot)
            ? Path.Combine(Path.GetTempPath(), "SnapStudio")
            : StorageRoot;
        string fileName = $"Recording-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.mp4";

        return Path.Combine(root, "Recordings", fileName);
    }

    private (bool IncludeMicrophoneAudio, bool IncludeSystemAudio) CreateScreenRecordingAudioFlags()
    {
        return ScreenRecordingAudioMode switch
        {
            ScreenRecordingAudioMode.Microphone => (true, false),
            ScreenRecordingAudioMode.SystemAudio => (false, true),
            _ => (false, false)
        };
    }

    private Dictionary<string, string> CreateScreenRecordingRequestDiagnostics(
        bool includeMicrophoneAudio,
        bool includeSystemAudio)
    {
        return new Dictionary<string, string>
        {
            ["requestedAudioMode"] = ScreenRecordingAudioMode.ToString(),
            ["requestedMicrophoneAudio"] = includeMicrophoneAudio.ToString(CultureInfo.InvariantCulture),
            ["requestedSystemAudio"] = includeSystemAudio.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static Dictionary<string, string> MergeScreenRecordingDiagnostics(
        IReadOnlyDictionary<string, string> requestDiagnostics,
        IReadOnlyDictionary<string, string>? resultDiagnostics)
    {
        var diagnostics = new Dictionary<string, string>(requestDiagnostics);
        if (resultDiagnostics is null)
        {
            return diagnostics;
        }

        foreach ((string key, string value) in resultDiagnostics)
        {
            diagnostics[key] = value;
        }

        return diagnostics;
    }

    private void ShowCaptureNotice(
        string title,
        string message,
        InfoBarSeverity severity)
    {
        CaptureNoticeIsOpen = false;
        CaptureNoticeTitle = title;
        CaptureNoticeMessage = message;
        CaptureNoticeSeverity = severity;
        CaptureNoticeIsOpen = true;
    }

    private void SetCaptureInProgress(bool isInProgress)
    {
        if (_captureInProgress == isInProgress)
        {
            return;
        }

        _captureInProgress = isInProgress;
        NotifyHistoryFolderStateChanged();
        OnPropertyChanged(nameof(CanCreateImage));
        OnPropertyChanged(nameof(CanPasteImageAsLayer));
        OnPropertyChanged(nameof(CanOpenImageFile));
        OnPropertyChanged(nameof(CanCaptureScrolling));
        OnPropertyChanged(nameof(CanToggleScreenRecording));
    }

    private void SetScreenRecordingOperationInProgress(bool isInProgress)
    {
        if (_screenRecordingOperationInProgress == isInProgress)
        {
            return;
        }

        _screenRecordingOperationInProgress = isInProgress;
        NotifyHistoryFolderStateChanged();
        OnPropertyChanged(nameof(CanCreateImage));
        OnPropertyChanged(nameof(CanPasteImageAsLayer));
        OnPropertyChanged(nameof(CanOpenImageFile));
        OnPropertyChanged(nameof(CanToggleScreenRecording));
        OnPropertyChanged(nameof(CanChangeScreenRecordingAudioMode));
    }

    private void InitializeFirstRunSetup(ApplicationSettings settings)
    {
        InitializeTheme(settings);
        FirstRunStorageRoot = settings.StorageRoot;
        FirstRunStorageBackendIndex = ResolveStorageBackendIndex(settings.StorageBackend);
        FirstRunCaptureHotkeyIndex = ResolveCaptureHotkeyIndex(settings.CaptureHotkey);
        FirstRunIncludeCursor = settings.IncludeCursorByDefault;
        FirstRunCopyCapturesToClipboard = settings.CopyCapturesToClipboard;
        InitializeSettingsFeatureFlags(settings);
        SettingsSurfaceIsFirstRun = !settings.FirstRunCompleted;
        FirstRunSetupIsOpen = !settings.FirstRunCompleted;
    }

    private async Task<bool> CopyCapturedDocumentIfNeededAsync(
        DocumentId documentId,
        CancellationToken cancellationToken)
    {
        if (!CopyCapturesToClipboard || _clipboardService is null)
        {
            return false;
        }

        ClipboardResult result = await _clipboardService
            .CopyDocumentAsync(documentId, cancellationToken)
            .ConfigureAwait(true);
        if (result.Succeeded)
        {
            return true;
        }

        ShowCaptureNotice(
            "Clipboard unavailable",
            result.ErrorMessage ?? "The capture was saved but could not be copied to the clipboard.",
            InfoBarSeverity.Warning);
        return false;
    }

    private async Task<CaptureDocument> SaveScrollingCaptureDocumentAsync(
        ScrollTargetCandidate target,
        ScrollingCaptureResult result,
        ImageAsset image,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        var metadata = new Dictionary<string, string>
        {
            ["title"] = result.Succeeded
                ? $"Scrolling Capture {capturedAtUtc.LocalDateTime:g}"
                : $"Partial Scrolling Capture {capturedAtUtc.LocalDateTime:g}",
            ["source"] = "scrollingCapture",
            ["scrollTargetId"] = target.Id,
            ["scrollTargetName"] = target.DisplayName,
            ["scrollTargetKind"] = target.Kind.ToString(),
            ["frameCount"] = result.Frames.Count.ToString(CultureInfo.InvariantCulture),
            ["isPartial"] = result.IsPartial.ToString(CultureInfo.InvariantCulture),
            ["failureReason"] = result.Failure?.Reason.ToString() ?? string.Empty,
            ["failureMessage"] = result.Failure?.Message ?? string.Empty
        };

        foreach ((string key, string value) in result.Diagnostics)
        {
            metadata[$"scrolling.{key}"] = value;
        }

        return await _documentRepository!.CreateFromCaptureAsync(
                new CaptureResult(
                    CaptureId.New(),
                    capturedAtUtc,
                    image,
                    metadata),
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task WriteScrollingCaptureDiagnosticAsync(
        bool succeeded,
        string? frameCount,
        string failureReason,
        string failureMessage,
        IReadOnlyDictionary<string, string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (_diagnosticLog is null)
        {
            return;
        }

        var properties = new Dictionary<string, string>(diagnostics)
        {
            ["succeeded"] = succeeded.ToString(CultureInfo.InvariantCulture),
            ["frameCount"] = frameCount ?? string.Empty,
            ["failureReason"] = failureReason,
            ["failureMessage"] = failureMessage
        };

        await _diagnosticLog
            .WriteAsync(
                DiagnosticEvent.Create(
                    succeeded ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                    "SnapStudio.App.ScrollingCapture",
                    succeeded ? "Scrolling capture completed." : "Scrolling capture completed with failure state.",
                    properties),
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task WriteScreenRecordingDiagnosticAsync(
        bool succeeded,
        string message,
        string outputPath,
        ScreenRecordingFailure? failure,
        IReadOnlyDictionary<string, string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (_diagnosticLog is null)
        {
            return;
        }

        var properties = new Dictionary<string, string>(diagnostics)
        {
            ["succeeded"] = succeeded.ToString(CultureInfo.InvariantCulture),
            ["outputPath"] = outputPath,
            ["failureReason"] = failure?.Reason.ToString() ?? string.Empty,
            ["failureMessage"] = failure?.Message ?? string.Empty
        };

        await _diagnosticLog
            .WriteAsync(
                DiagnosticEvent.Create(
                    succeeded ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                    "SnapStudio.App.ScreenRecording",
                    message,
                    properties),
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task OpenMostRecentDocumentAsync(CancellationToken cancellationToken)
    {
        DocumentSummaryItem? document = RecentDocuments.FirstOrDefault();
        if (document is not null)
        {
            await OpenDocumentAsync(document.Id, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task OpenDocumentAsync(DocumentId documentId, CancellationToken cancellationToken)
    {
        if (_documentRepository is null)
        {
            return;
        }

        CaptureDocument? document;
        try
        {
            document = await _documentRepository
                .GetAsync(documentId, cancellationToken)
                .ConfigureAwait(true);
            if (document is not null)
            {
                DocumentLayers.EnsureInitialized(document);
            }
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException)
        {
            string message = exception is NotSupportedException
                ? "This document was created by a newer version of SnapStudio. Update the app to open it."
                : "This document has invalid layer data and could not be opened.";
            StatusText = message;
            ShowCaptureNotice("Open failed", message, InfoBarSeverity.Error);
            return;
        }

        if (document is null)
        {
            StatusText = "Document could not be opened.";
            return;
        }

        // Replace the old fixed text rectangles with their intrinsic content bounds in the workspace.
        foreach (AnnotationObject annotation in document.Annotations.Where(item => item.Kind == AnnotationKind.Text))
            annotation.Bounds = TextAnnotationLayout.FitBounds(annotation.Bounds, annotation.Text, annotation.Style.StrokeThickness, annotation.Style.FontFamily);
        _pendingDocumentSave = Task.CompletedTask;
        _currentDocument = document;
        _layerWorkspace.OpenDocument(document);
        _currentDocumentId = document.Id;
        _editCommandStack?.Clear();
        _annotationSelection.Clear();
        SetCanvasViewport(CanvasViewportState.Create(
            document.SourceImage.Width,
            document.SourceImage.Height));
        _canvasFitToViewport = true;
        CurrentDocumentTitle = ResolveDocumentTitle(document);
        WorkspaceTitleText = CurrentDocumentTitle;
        CurrentDocumentDetail = CreateDocumentDetail(document);
        UpdateCurrentDocumentProperties(document);
        SyncResizeDimensionsFromDocument(document);
        RefreshAnnotationOverlays();
        NotifyDocumentWorkspaceStateChanged();
        NotifyEditorStateChanged();
        await WriteDocumentRecoveryEntryAsync(
                RecoveryJournalEventKind.DocumentOpened,
                document,
                "Document opened.",
                cancellationToken)
            .ConfigureAwait(true);

        if (File.Exists(document.SourceImage.Path))
        {
            CanvasImageSource = new BitmapImage(new Uri(document.SourceImage.Path));
            StatusText = $"Opened {CurrentDocumentTitle}.";
            return;
        }

        CanvasImageSource = null;
        CurrentDocumentDetail = "Capture image is not available yet";
    }

    private async Task SaveCurrentDocumentAsync(
        string statusText,
        CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _documentRepository is null)
        {
            return;
        }

        _documentChangeVersion++;
        Task save = _documentRepository.SaveAsync(_currentDocument, cancellationToken);
        _pendingDocumentSave = save;
        await save.ConfigureAwait(true);

        await WriteDocumentRecoveryEntryAsync(
                RecoveryJournalEventKind.DocumentSaved,
                _currentDocument,
                statusText,
                cancellationToken)
            .ConfigureAwait(true);

        CurrentDocumentDetail = CreateDocumentDetail(_currentDocument);
        UpdateCurrentDocumentProperties(_currentDocument);
        RefreshAnnotationOverlays();
        NotifyEditorStateChanged();
        await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
        StatusText = statusText;
    }

    private async Task InvalidateOcrCacheAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is null || _ocrTextExtractionService is null)
        {
            return;
        }

        try
        {
            await _ocrTextExtractionService
                .InvalidateAsync(_currentDocument.Id, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (_diagnosticLog is not null)
            {
                await _diagnosticLog
                    .WriteAsync(
                        DiagnosticEvent.Create(
                            DiagnosticSeverity.Warning,
                            "SnapStudio.App.Ocr",
                            "OCR cache invalidation failed.",
                            new Dictionary<string, string>
                            {
                                ["documentId"] = _currentDocument.Id.ToString(),
                                ["error"] = exception.Message
                            }),
                        cancellationToken)
                    .ConfigureAwait(true);
            }
        }
    }

    private async Task ClearCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_currentDocument is not null)
        {
            await WriteDocumentRecoveryEntryAsync(
                    RecoveryJournalEventKind.DocumentCleared,
                    _currentDocument,
                    "Current document cleared.",
                    cancellationToken)
                .ConfigureAwait(true);
        }

        _currentDocument = null;
        _layerWorkspace.ClearDocument();
        _currentDocumentId = null;
        _editCommandStack?.Clear();
        _annotationSelection.Clear();
        _canvasFitToViewport = true;
        WorkspaceTitleText = string.Empty;
        CurrentDocumentTitle = "Canvas";
        CurrentDocumentDetail = "No document selected";
        UpdateCurrentDocumentProperties(null);
        SetResizeValues(1, 1);
        CanvasImageSource = null;
        SetCanvasViewport(CanvasViewportState.Empty);
        RefreshAnnotationOverlays();
        NotifyDocumentWorkspaceStateChanged();
        NotifyEditorStateChanged();
    }

    private Task WriteDocumentRecoveryEntryAsync(
        RecoveryJournalEventKind eventKind,
        CaptureDocument document,
        string message,
        CancellationToken cancellationToken)
    {
        if (_crashRecoveryJournal is null || string.IsNullOrWhiteSpace(_recoverySessionId))
        {
            return Task.CompletedTask;
        }

        return _crashRecoveryJournal.WriteAsync(
            RecoveryJournalEntry.Create(
                _recoverySessionId,
                eventKind,
                message,
                document.Id.ToString(),
                ResolveDocumentTitle(document),
                document.Metadata.ModifiedAtUtc,
                document.Annotations.Count,
                new Dictionary<string, string>
                {
                    ["sourceImagePath"] = document.SourceImage.Path,
                    ["sourceWidth"] = document.SourceImage.Width.ToString(CultureInfo.InvariantCulture),
                    ["sourceHeight"] = document.SourceImage.Height.ToString(CultureInfo.InvariantCulture),
                    ["schemaVersion"] = document.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                }),
            cancellationToken);
    }

    private async Task ApplySelectedAnnotationStyleAsync(
        string displayName,
        CancellationToken cancellationToken,
        SelectionStyleAspect aspect = SelectionStyleAspect.All)
    {
        if (!CanApplyAnnotationProperty(aspect)) return;
        if (SelectedAnnotationCount > 1)
        {
            await ApplySelectionStyleAsync(aspect, cancellationToken);
            return;
        }
        if (_currentDocument is null
            || _documentRepository is null
            || _annotationEditor is null
            || _annotationSelection.SelectedAnnotationId is not Guid selectedId)
        {
            return;
        }

        AnnotationObject? annotation = _currentDocument.Annotations
            .FirstOrDefault(candidate => candidate.Id == selectedId);
        if (annotation is null)
        {
            _annotationSelection.Clear();
            RefreshAnnotationOverlays();
            NotifyEditorStateChanged();
            return;
        }

        AnnotationStyle before = annotation.Style;
        if (aspect == SelectionStyleAspect.Font && annotation.Kind != AnnotationKind.Text) return;
        AnnotationStyle after = aspect == SelectionStyleAspect.Font
            ? before with { FontFamily = AnnotationFontFamily } : CreateUpdatedStyle(annotation.Kind, before);

        if (StylesAreEquivalent(before, after))
        {
            return;
        }

        await _annotationEditor.SetStyleAsync(
            _currentDocument,
            annotation,
            after,
            displayName,
            cancellationToken,
            annotation.Kind == AnnotationKind.Text
                ? TextAnnotationLayout.FitBounds(annotation.Bounds, annotation.Text, after.StrokeThickness, after.FontFamily) : null).ConfigureAwait(true);

        if (!SelectedAnnotationIds.Contains(annotation.Id)) _annotationSelection.Select(_currentDocument, annotation.Id);
        RefreshAnnotationOverlays();
        NotifyEditorStateChanged();
        await SaveCurrentDocumentAsync($"{displayName} complete.", cancellationToken)
            .ConfigureAwait(true);
    }

    private void SetCanvasViewport(CanvasViewportState viewport)
    {
        if (_canvasViewport == viewport)
        {
            return;
        }

        _canvasViewport = viewport;
        RefreshAnnotationOverlays();
        NotifyViewportChanged();
    }

    private void RefreshAnnotationOverlays()
    {
        _layerWorkspace.Refresh(
            _currentDocument,
            _canvasViewport.Zoom,
            _annotationSelection.SelectedAnnotationIds,
            _canvasViewport.HasSource,
            showTransformHandles: IsMoveToolActive);

        OnPropertyChanged(nameof(AnnotationOverlays));
        OnPropertyChanged(nameof(AnnotationLayerCount));
        OnPropertyChanged(nameof(AnnotationLayersEmptyVisibility));
        OnPropertyChanged(nameof(AnnotationLayersEmptyMessage));
    }

    private void NotifyViewportChanged()
    {
        OnPropertyChanged(nameof(CanvasDisplayWidth));
        OnPropertyChanged(nameof(CanvasDisplayHeight));
        OnPropertyChanged(nameof(CanvasZoomLabel));
        OnPropertyChanged(nameof(CanFitCanvas));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    private void NotifyDocumentWorkspaceStateChanged()
    {
        OnPropertyChanged(nameof(CanEditCurrentCaptureTags));
        OnPropertyChanged(nameof(CanMoveCurrentCaptureToHistoryFolder));
        OnPropertyChanged(nameof(HasCurrentDocument));
        OnPropertyChanged(nameof(CanPasteImageAsLayer));
        OnPropertyChanged(nameof(CanRenameCurrentDocument));
        OnPropertyChanged(nameof(CanDuplicateCurrentDocument));
        OnPropertyChanged(nameof(CanDeleteCurrentDocument));
        OnPropertyChanged(nameof(CanMoveCurrentCaptureToTrash));
        OnPropertyChanged(nameof(CanOpenCurrentDocumentFolder));
        OnPropertyChanged(nameof(CanPinCurrentDocument));
        OnPropertyChanged(nameof(CanCopyRecognizedText));
        OnPropertyChanged(nameof(CanCaptureScrolling));
        OnPropertyChanged(nameof(CanToggleScreenRecording));
    }

    private void NotifyScreenRecordingStateChanged()
    {
        NotifyHistoryFolderStateChanged();
        OnPropertyChanged(nameof(CanCreateImage));
        OnPropertyChanged(nameof(CanPasteImageAsLayer));
        OnPropertyChanged(nameof(CanOpenImageFile));
        OnPropertyChanged(nameof(IsScreenRecording));
        OnPropertyChanged(nameof(ScreenRecordingCommandText));
        OnPropertyChanged(nameof(ScreenRecordingAutomationName));
        OnPropertyChanged(nameof(ScreenRecordingIndicatorVisibility));
        OnPropertyChanged(nameof(ScreenRecordingIndicatorText));
        OnPropertyChanged(nameof(CanToggleScreenRecording));
        OnPropertyChanged(nameof(CanChangeScreenRecordingAudioMode));
    }

    private void NotifyEditorStateChanged()
    {
        NotifyAnnotationStyleControlsChanged();
        OnPropertyChanged(nameof(CanAddActiveAnnotation));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanDeleteActiveLayer));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
        OnPropertyChanged(nameof(HasSelectedAnnotation));
        OnPropertyChanged(nameof(HasCanvasSelection));
        OnPropertyChanged(nameof(SelectionDisplayBounds));
        OnPropertyChanged(nameof(SelectedAnnotationCount));
        OnPropertyChanged(nameof(CanEditAnnotationText));
        OnPropertyChanged(nameof(ActiveAnnotationToolLabel));
        OnPropertyChanged(nameof(CanDeleteSelectedAnnotation));
        OnPropertyChanged(nameof(CanCropToSelection));
        OnPropertyChanged(nameof(CanCopyRecognizedText));
    }

    private void SyncStyleControlsFromSelectedAnnotation()
    {
        AnnotationObject? annotation = GetSelectedAnnotation();
        if (annotation is null)
        {
            return;
        }

        MarkAnnotationPresetCustom();
        SelectAnnotationColor(annotation.Style.Stroke);
        AnnotationStrokeThickness = annotation.Style.StrokeThickness;
        AnnotationCornerRadius = annotation.Style.CornerRadius;
        AnnotationOpacity = annotation.Style.Opacity;
        AnnotationPattern = annotation.Style.Pattern;
        _annotationDesignFill = annotation.Style.Fill;
        SyncAnnotationFontFromSelection();
        if (annotation.Kind == AnnotationKind.Text)
        {
            AnnotationText = annotation.Text ?? "Text";
            SelectAnnotationColor(annotation.Style.Text);
        }
        else if (annotation.Kind is AnnotationKind.Highlight or AnnotationKind.Fill)
        {
            SelectAnnotationColor(annotation.Style.Fill);
        }

        AnnotationToolIndex = annotation.Kind switch
        {
            AnnotationKind.Ellipse => 1,
            AnnotationKind.Line => 2,
            AnnotationKind.Arrow => 3,
            AnnotationKind.Text => 4,
            AnnotationKind.Highlight => 5,
            AnnotationKind.Blur => 6,
            AnnotationKind.Pen => 7,
            AnnotationKind.Fill => 8,
            AnnotationKind.Image => AnnotationToolIndex,
            _ => 0
        };
        NotifyAnnotationStyleControlsChanged();
    }

    private AnnotationObject? GetSelectedAnnotation()
    {
        if (_currentDocument is null
            || _annotationSelection.SelectedAnnotationId is not Guid selectedId)
        {
            return null;
        }

        return _currentDocument.Annotations
            .FirstOrDefault(annotation => annotation.Id == selectedId);
    }

    private void RefreshCanvasAfterRasterEdit()
    {
        if (_currentDocument is null)
        {
            return;
        }

        SetCanvasViewport(CanvasViewportState.Create(
            _currentDocument.SourceImage.Width,
            _currentDocument.SourceImage.Height));
        _canvasFitToViewport = true;
        SyncResizeDimensionsFromDocument(_currentDocument);

        CanvasImageSource = File.Exists(_currentDocument.SourceImage.Path)
            ? new BitmapImage(new Uri(_currentDocument.SourceImage.Path))
            : null;
    }

    private void SetResizeValues(double width, double height)
    {
        SetResizeWidth(width);
        SetResizeHeight(height);
    }

    private void SetResizeWidth(double value)
    {
        double normalized = NormalizeResizeValue(value);
        if (Math.Abs(_resizeWidth - normalized) < 0.01)
        {
            return;
        }

        _resizeWidth = normalized;
        OnPropertyChanged(nameof(ResizeWidth));
    }

    private void SetResizeHeight(double value)
    {
        double normalized = NormalizeResizeValue(value);
        if (Math.Abs(_resizeHeight - normalized) < 0.01)
        {
            return;
        }

        _resizeHeight = normalized;
        OnPropertyChanged(nameof(ResizeHeight));
    }

    private void SyncResizeDimensionsFromDocument(CaptureDocument document)
    {
        NotifyResizeUnitChanged();
        SetResizeValuesFromTargetDimensions(new ResizeDimensions(
            document.SourceImage.Width,
            document.SourceImage.Height));
    }

    private void SetResizeValuesFromTargetDimensions(ResizeDimensions targetDimensions)
    {
        if (ResizeUsesPercent
            && _currentDocument is { SourceImage.Width: > 0, SourceImage.Height: > 0 } document)
        {
            double widthPercent = NormalizeResizePercentValue(
                targetDimensions.Width / document.SourceImage.Width * 100);
            double heightPercent = ResizeAspectRatioIsLocked
                ? widthPercent
                : NormalizeResizePercentValue(targetDimensions.Height / document.SourceImage.Height * 100);

            SetResizeValues(widthPercent, heightPercent);
            return;
        }

        SetResizeValues(targetDimensions.Width, targetDimensions.Height);
    }

    private ResizeDimensions CreateTargetResizeDimensions()
    {
        if (ResizeUsesPercent
            && _currentDocument is { SourceImage.Width: > 0, SourceImage.Height: > 0 } document)
        {
            double widthPercent = NormalizeResizePercentValue(_resizeWidth);
            double heightPercent = NormalizeResizePercentValue(_resizeHeight);

            return new ResizeDimensions(
                NormalizeResizePixelDimension(document.SourceImage.Width * widthPercent / 100),
                NormalizeResizePixelDimension(document.SourceImage.Height * heightPercent / 100));
        }

        return new ResizeDimensions(
            NormalizeResizePixelDimension(_resizeWidth),
            NormalizeResizePixelDimension(_resizeHeight));
    }

    private void NotifyResizeUnitChanged()
    {
        OnPropertyChanged(nameof(ResizeWidthHeader));
        OnPropertyChanged(nameof(ResizeHeightHeader));
        OnPropertyChanged(nameof(ResizeMaximumValue));
        OnPropertyChanged(nameof(ResizeStepFrequency));
    }

    private bool TryGetResizeAspectRatio(out double aspectRatio)
    {
        aspectRatio = 0;

        if (_currentDocument is { SourceImage.Width: > 0, SourceImage.Height: > 0 } document)
        {
            aspectRatio = (double)document.SourceImage.Width / document.SourceImage.Height;
            return true;
        }

        if (_resizeWidth > 0 && _resizeHeight > 0)
        {
            aspectRatio = _resizeWidth / _resizeHeight;
            return aspectRatio > 0;
        }

        return false;
    }

    private static ResizeDimensions CreateResizeDimensionsFromWidth(
        double requestedWidth,
        double aspectRatio)
    {
        double width = NormalizeResizePixelDimension(requestedWidth);
        double height = width / aspectRatio;

        if (height > ResizeMaximumDimension)
        {
            height = ResizeMaximumDimension;
            width = height * aspectRatio;
        }
        else if (height < ResizeMinimumDimension)
        {
            height = ResizeMinimumDimension;
            width = height * aspectRatio;
        }

        return new ResizeDimensions(
            NormalizeResizePixelDimension(width),
            NormalizeResizePixelDimension(height));
    }

    private static ResizeDimensions CreateResizeDimensionsFromHeight(
        double requestedHeight,
        double aspectRatio)
    {
        double height = NormalizeResizePixelDimension(requestedHeight);
        double width = height * aspectRatio;

        if (width > ResizeMaximumDimension)
        {
            width = ResizeMaximumDimension;
            height = width / aspectRatio;
        }
        else if (width < ResizeMinimumDimension)
        {
            width = ResizeMinimumDimension;
            height = width / aspectRatio;
        }

        return new ResizeDimensions(
            NormalizeResizePixelDimension(width),
            NormalizeResizePixelDimension(height));
    }

    private double NormalizeResizeValue(double value)
    {
        return ResizeUsesPercent
            ? NormalizeResizePercentValue(value)
            : NormalizeResizePixelDimension(value);
    }

    private double NormalizeResizePercentValue(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(Math.Round(value), ResizeMinimumPercent, GetResizeMaximumPercent())
            : ResizeMinimumPercent;
    }

    private double GetResizeMaximumPercent()
    {
        if (_currentDocument is not { SourceImage.Width: > 0, SourceImage.Height: > 0 } document)
        {
            return ResizeMaximumPercent;
        }

        double maximumWidthPercent = ResizeMaximumDimension / document.SourceImage.Width * 100;
        double maximumHeightPercent = ResizeMaximumDimension / document.SourceImage.Height * 100;
        double maximumPercent = Math.Min(ResizeMaximumPercent, Math.Min(maximumWidthPercent, maximumHeightPercent));
        return Math.Max(ResizeMinimumPercent, Math.Floor(maximumPercent));
    }

    private static double NormalizeResizePixelDimension(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(Math.Round(value), ResizeMinimumDimension, ResizeMaximumDimension)
            : ResizeMinimumDimension;
    }

    private static string ResolveRasterOutputDirectory(CaptureDocument document)
    {
        string? sourceDirectory = Path.GetDirectoryName(document.SourceImage.Path);
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? Path.Combine(Path.GetTempPath(), "SnapStudio", "RasterEdits")
            : sourceDirectory;
    }

    private static List<AnnotationObject> TransformAnnotationsForCrop(
        IReadOnlyCollection<AnnotationObject> annotations,
        RectD cropBounds)
    {
        RectD normalizedCropBounds = Normalize(cropBounds);
        var transformed = new List<AnnotationObject>();

        foreach (AnnotationObject annotation in annotations)
        {
            AnnotationObject? next = TransformAnnotationForCrop(annotation, normalizedCropBounds);
            if (next is not null)
            {
                transformed.Add(next);
            }
        }

        return transformed;
    }

    private static AnnotationObject? TransformAnnotationForCrop(
        AnnotationObject annotation,
        RectD cropBounds)
    {
        if (annotation.Kind is AnnotationKind.Pen or AnnotationKind.Fill or AnnotationKind.Image)
        {
            // Keep image, fill and stroke geometry intact; the cropped canvas clips the visible portion.
            RectD bounds = annotation.Bounds;
            double radius = annotation.Style.StrokeThickness / 2;
            if (bounds.Right + radius < cropBounds.X || bounds.X - radius > cropBounds.Right
                || bounds.Bottom + radius < cropBounds.Y || bounds.Y - radius > cropBounds.Bottom)
            {
                return null;
            }
            return CloneAnnotation(annotation, bounds with { X = bounds.X - cropBounds.X, Y = bounds.Y - cropBounds.Y });
        }
        if (annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            if (AnnotationBoundsEditor.ClipLine(annotation.Bounds, cropBounds) is not RectD clipped) return null;
            RectD bounds = clipped with { X = clipped.X - cropBounds.X, Y = clipped.Y - cropBounds.Y };

            return Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) < 1
                ? null
                : CloneAnnotation(annotation, bounds);
        }

        RectD normalized = Normalize(annotation.Bounds);
        double left = Math.Max(normalized.X, cropBounds.X);
        double top = Math.Max(normalized.Y, cropBounds.Y);
        double right = Math.Min(normalized.Right, cropBounds.Right);
        double bottom = Math.Min(normalized.Bottom, cropBounds.Bottom);

        if (right - left < 1 || bottom - top < 1)
        {
            return null;
        }

        return CloneAnnotation(annotation, new RectD(
            left - cropBounds.X,
            top - cropBounds.Y,
            right - left,
            bottom - top));
    }

    private static List<AnnotationObject> TransformAnnotationsForResize(
        IReadOnlyCollection<AnnotationObject> annotations,
        double scaleX,
        double scaleY)
    {
        return annotations
            .Select(annotation => CloneAnnotation(
                annotation,
                new RectD(
                    annotation.Bounds.X * scaleX,
                    annotation.Bounds.Y * scaleY,
                    annotation.Bounds.Width * scaleX,
                    annotation.Bounds.Height * scaleY)))
            .ToList();
    }

    private static List<AnnotationObject> CloneAnnotations(
        IReadOnlyCollection<AnnotationObject> annotations)
    {
        return annotations
            .Select(annotation => CloneAnnotation(annotation, annotation.Bounds))
            .ToList();
    }

    private static AnnotationObject CloneAnnotation(
        AnnotationObject annotation,
        RectD bounds,
        bool copyImageBytes = true)
    {
        return new AnnotationObject
        {
            Id = annotation.Id,
            Kind = annotation.Kind,
            LayerId = annotation.LayerId,
            IsVisible = annotation.IsVisible,
            Bounds = bounds,
            Text = annotation.Text,
            StrokePoints = [.. annotation.StrokePoints],
            FillRectangles = [.. annotation.FillRectangles],
            Image = copyImageBytes ? annotation.Image?.DeepCopy() : annotation.Image,
            Vector = annotation.Vector,
            Style = annotation.Style
        };
    }

    private AnnotationObject CreateCenteredAnnotation(
        CaptureDocument document,
        AnnotationKind annotationKind)
    {
        double width = ResolveDefaultAnnotationLength(document.SourceImage.Width, 0.35, 80);
        double height = ResolveDefaultAnnotationLength(document.SourceImage.Height, 0.25, 60);

        AnnotationObject annotation = CreateAnnotation(
            document,
            annotationKind,
            new RectD(
                (document.SourceImage.Width - width) / 2,
                (document.SourceImage.Height - height) / 2,
                width,
                height));
        if (annotationKind == AnnotationKind.Text)
            annotation.Bounds = annotation.Bounds with
            {
                X = (document.SourceImage.Width - annotation.Bounds.Width) / 2,
                Y = (document.SourceImage.Height - annotation.Bounds.Height) / 2
            };
        return annotation;
    }

    private AnnotationObject CreateAnnotation(
        CaptureDocument document,
        AnnotationKind annotationKind,
        RectD sourceBounds)
    {
        AnnotationObject annotation = new()
        {
            Kind = annotationKind,
            Bounds = NormalizeAndClampSourceBounds(document, sourceBounds, annotationKind),
            Text = annotationKind == AnnotationKind.Text ? AnnotationText : null,
            Vector = annotationKind == AnnotationKind.Vector && ActiveSvgToolDesign is { Svg: not null } design ? new(design.Svg, design.Name) : null,
            Style = CreateAnnotationStyle(annotationKind)
        };
        if (annotationKind == AnnotationKind.Text)
            annotation.Bounds = TextAnnotationLayout.FitBounds(annotation.Bounds, annotation.Text, annotation.Style.StrokeThickness, annotation.Style.FontFamily);
        return annotation;
    }

    private static bool TextSizeChanged(RectD before, RectD after) =>
        Math.Abs(before.Width - after.Width) > 0.01 || Math.Abs(before.Height - after.Height) > 0.01;

    private static RectD NormalizeAndClampSourceBounds(
        CaptureDocument document,
        RectD sourceBounds,
        AnnotationKind annotationKind)
    {
        if (annotationKind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            double startX = Math.Clamp(sourceBounds.X, 0, document.SourceImage.Width);
            double startY = Math.Clamp(sourceBounds.Y, 0, document.SourceImage.Height);
            double endX = Math.Clamp(sourceBounds.Right, 0, document.SourceImage.Width);
            double endY = Math.Clamp(sourceBounds.Bottom, 0, document.SourceImage.Height);

            return new RectD(startX, startY, endX - startX, endY - startY);
        }

        double left = Math.Min(sourceBounds.X, sourceBounds.Right);
        double top = Math.Min(sourceBounds.Y, sourceBounds.Bottom);
        double right = Math.Max(sourceBounds.X, sourceBounds.Right);
        double bottom = Math.Max(sourceBounds.Y, sourceBounds.Bottom);

        left = Math.Clamp(left, 0, document.SourceImage.Width);
        top = Math.Clamp(top, 0, document.SourceImage.Height);
        right = Math.Clamp(right, left, document.SourceImage.Width);
        bottom = Math.Clamp(bottom, top, document.SourceImage.Height);

        return new RectD(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static bool TryCreateSquareResizeBounds(
        RectD originalSourceBounds,
        AnnotationBoundsHandle handle,
        PointD currentSourcePoint,
        SizeD sourceSize,
        out RectD bounds)
    {
        bounds = default;
        if (!IsCornerResizeHandle(handle))
        {
            return false;
        }

        RectD normalized = Normalize(originalSourceBounds);
        PointD anchor = handle switch
        {
            AnnotationBoundsHandle.TopLeft => new PointD(normalized.Right, normalized.Bottom),
            AnnotationBoundsHandle.TopRight => new PointD(normalized.X, normalized.Bottom),
            AnnotationBoundsHandle.BottomRight => new PointD(normalized.X, normalized.Y),
            AnnotationBoundsHandle.BottomLeft => new PointD(normalized.Right, normalized.Y),
            _ => new PointD(normalized.X, normalized.Y)
        };

        double directionX = currentSourcePoint.X >= anchor.X ? 1 : -1;
        double directionY = currentSourcePoint.Y >= anchor.Y ? 1 : -1;
        double requestedSide = Math.Min(
            Math.Abs(currentSourcePoint.X - anchor.X),
            Math.Abs(currentSourcePoint.Y - anchor.Y));
        double maximumSideX = directionX > 0
            ? sourceSize.Width - anchor.X
            : anchor.X;
        double maximumSideY = directionY > 0
            ? sourceSize.Height - anchor.Y
            : anchor.Y;
        double side = Math.Clamp(
            requestedSide,
            AnnotationBoundsEditor.MinimumSize,
            Math.Max(AnnotationBoundsEditor.MinimumSize, Math.Min(maximumSideX, maximumSideY)));
        double targetX = anchor.X + directionX * side;
        double targetY = anchor.Y + directionY * side;
        double left = Math.Min(anchor.X, targetX);
        double top = Math.Min(anchor.Y, targetY);

        bounds = new RectD(left, top, side, side);
        return true;
    }

    private static bool IsCornerResizeHandle(AnnotationBoundsHandle handle)
    {
        return handle is AnnotationBoundsHandle.TopLeft
            or AnnotationBoundsHandle.TopRight
            or AnnotationBoundsHandle.BottomRight
            or AnnotationBoundsHandle.BottomLeft;
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static bool BoundsAreEquivalent(RectD first, RectD second)
    {
        const double tolerance = 0.1;

        return Math.Abs(first.X - second.X) < tolerance
            && Math.Abs(first.Y - second.Y) < tolerance
            && Math.Abs(first.Width - second.Width) < tolerance
            && Math.Abs(first.Height - second.Height) < tolerance;
    }

    private static bool StylesAreEquivalent(AnnotationStyle first, AnnotationStyle second)
    {
        const double tolerance = 0.01;

        return first.Stroke == second.Stroke
            && first.Fill == second.Fill
            && first.Text == second.Text
            && first.Pattern == second.Pattern
            && string.Equals(first.FontFamily, second.FontFamily, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(first.StrokeThickness - second.StrokeThickness) < tolerance
            && Math.Abs(first.CornerRadius - second.CornerRadius) < tolerance
            && Math.Abs(first.Opacity - second.Opacity) < tolerance;
    }

    private static double ResolveDefaultAnnotationLength(
        int sourceLength,
        double proportion,
        double preferredMinimum)
    {
        if (sourceLength <= 0)
        {
            return 1;
        }

        double minimum = Math.Min(preferredMinimum, sourceLength);
        return Math.Min(sourceLength, Math.Max(sourceLength * proportion, minimum));
    }

    private AnnotationStyle CreateAnnotationStyle(AnnotationKind annotationKind)
    {
        ColorRgba color = ResolveStyleColor();
        double opacity = ResolveAnnotationOpacity(annotationKind);

        return annotationKind switch
        {
            AnnotationKind.Vector => new AnnotationStyle(ColorRgba.Transparent, ColorRgba.Transparent, ColorRgba.Black, 1, opacity),
            AnnotationKind.Fill => new AnnotationStyle(ColorRgba.Transparent, color, ColorRgba.Black, 1, opacity),
            AnnotationKind.Text => new AnnotationStyle(
                ColorRgba.Transparent,
                ColorRgba.Transparent,
                color,
                AnnotationStrokeThickness,
                opacity)
            { FontFamily = AnnotationFontFamily },
            AnnotationKind.Highlight => new AnnotationStyle(
                ColorRgba.Transparent,
                ResolveFilledToolColor(annotationKind, color),
                ColorRgba.Black,
                1,
                opacity),
            AnnotationKind.Blur => new AnnotationStyle(
                ColorRgba.Transparent,
                ColorRgba.Transparent,
                ColorRgba.Black,
                1,
                opacity),
            _ => new AnnotationStyle(
                color,
                annotationKind is AnnotationKind.Rectangle or AnnotationKind.Ellipse ? _annotationDesignFill : ColorRgba.Transparent,
                ColorRgba.Black,
                AnnotationStrokeThickness,
                opacity,
                annotationKind == AnnotationKind.Rectangle ? AnnotationCornerRadius : 0)
            { Pattern = AnnotationPattern }
        };
    }

    private AnnotationStyle CreateUpdatedStyle(
        AnnotationKind annotationKind,
        AnnotationStyle before)
    {
        ColorRgba color = ResolveStyleColor();

        return annotationKind switch
        {
            AnnotationKind.Text => before with
            {
                Text = color,
                StrokeThickness = AnnotationStrokeThickness,
                Opacity = AnnotationOpacity
            },
            AnnotationKind.Highlight or AnnotationKind.Fill => before with
            {
                Fill = color,
                Opacity = AnnotationOpacity
            },
            AnnotationKind.Image => before with { Opacity = AnnotationOpacity },
            AnnotationKind.Blur => before with
            {
                Stroke = ColorRgba.Transparent,
                Fill = ColorRgba.Transparent,
                Opacity = AnnotationOpacity
            },
            _ => before with
            {
                Stroke = color,
                Pattern = AnnotationPattern,
                StrokeThickness = AnnotationStrokeThickness,
                Opacity = AnnotationOpacity,
                CornerRadius = annotationKind == AnnotationKind.Rectangle
                    ? AnnotationCornerRadius
                    : before.CornerRadius
            }
        };
    }

    private double ResolveAnnotationOpacity(AnnotationKind annotationKind)
    {
        if (_annotationSelection.HasSelection || _chosenToolDesigns.ContainsKey(annotationKind) || Math.Abs(AnnotationOpacity - 1) >= 0.01)
        {
            return AnnotationOpacity;
        }

        return annotationKind switch
        {
            AnnotationKind.Highlight => 0.35,
            AnnotationKind.Blur => 0.65,
            _ => AnnotationOpacity
        };
    }

    private void ApplyAnnotationPresetDefaults(int presetIndex)
    {
        AnnotationPattern = StrokePattern.Solid;
        switch (presetIndex)
        {
            case 1:
                AnnotationToolIndex = 0;
                AnnotationStrokeIndex = 0;
                AnnotationStrokeThickness = 3;
                AnnotationCornerRadius = 0;
                AnnotationOpacity = 1;
                break;
            case 2:
                AnnotationToolIndex = 0;
                AnnotationStrokeIndex = 2;
                AnnotationStrokeThickness = 4;
                AnnotationCornerRadius = 14;
                AnnotationOpacity = 1;
                break;
            case 3:
                AnnotationToolIndex = 3;
                AnnotationStrokeIndex = 0;
                AnnotationStrokeThickness = 4;
                AnnotationOpacity = 1;
                break;
            case 4:
                AnnotationToolIndex = 4;
                AnnotationStrokeIndex = 1;
                AnnotationStrokeThickness = 18;
                AnnotationOpacity = 1;
                break;
            case 5:
                AnnotationToolIndex = 5;
                AnnotationStrokeIndex = 2;
                AnnotationStrokeThickness = 1;
                AnnotationOpacity = 0.35;
                break;
            case 6:
                AnnotationToolIndex = 6;
                AnnotationStrokeIndex = 4;
                AnnotationStrokeThickness = 1;
                AnnotationOpacity = 0.65;
                break;
            default:
                AnnotationPresetIndex = 0;
                break;
        }
        _chosenToolDesigns.Remove(ActiveToolFamily);
        _annotationDesignFill = ColorRgba.Transparent;
    }

    private static string ResolveAnnotationPresetName(int presetIndex)
    {
        return presetIndex switch
        {
            1 => "Standard box",
            2 => "Emphasis box",
            3 => "Pointer arrow",
            4 => "Text label",
            5 => "Highlighter",
            6 => "Blur region",
            _ => "Custom"
        };
    }

    private void MarkAnnotationPresetCustom()
    {
        AnnotationPresetIndex = 0;
    }

    private ColorRgba ResolveFilledToolColor(
        AnnotationKind annotationKind,
        ColorRgba selectedColor)
    {
        if (_annotationSelection.HasSelection || _chosenToolDesigns.ContainsKey(annotationKind) || AnnotationStrokeIndex != 0)
        {
            return selectedColor;
        }

        return annotationKind switch
        {
            AnnotationKind.Highlight => new ColorRgba(255, 184, 92, 255),
            _ => selectedColor
        };
    }

    private void ApplyAnnotationToolDefaultsWhenUnselected()
    {
        if (_annotationSelection.HasSelection)
        {
            return;
        }

        switch (ActiveAnnotationKind)
        {
            case AnnotationKind.Highlight:
                AnnotationStrokeIndex = 2;
                AnnotationOpacity = 0.35;
                break;
            case AnnotationKind.Blur:
                AnnotationOpacity = 0.65;
                break;
            case AnnotationKind.Text:
                AnnotationStrokeIndex = 0;
                AnnotationOpacity = 1;
                AnnotationStrokeThickness = Math.Max(16, AnnotationStrokeThickness);
                break;
            default:
                if (AnnotationStrokeIndex is 3 or 4)
                {
                    AnnotationStrokeIndex = 0;
                }

                AnnotationOpacity = 1;
                break;
        }
        ApplyRememberedToolDesign();
    }

    private ColorRgba ResolveStyleColor()
    {
        return AnnotationStrokeIndex switch
        {
            1 => ColorRgba.White,
            2 => new ColorRgba(255, 184, 92, 255),
            3 => new ColorRgba(255, 116, 125, 255),
            4 => new ColorRgba(148, 220, 177, 255),
            AnnotationStrokeCustomIndex => _customAnnotationColor,
            _ => new ColorRgba(76, 194, 255, 255)
        };
    }

    private void SelectAnnotationColor(ColorRgba color)
    {
        int strokeIndex = ResolveStrokeIndex(color);
        if (strokeIndex == AnnotationStrokeCustomIndex)
        {
            _customAnnotationColor = color;
        }

        AnnotationStrokeIndex = strokeIndex;
    }

    private static int ResolveStrokeIndex(ColorRgba stroke)
    {
        if (stroke == ColorRgba.White)
        {
            return 1;
        }

        if (stroke == new ColorRgba(255, 184, 92, 255))
        {
            return 2;
        }

        if (stroke == new ColorRgba(255, 116, 125, 255))
        {
            return 3;
        }

        if (stroke == new ColorRgba(148, 220, 177, 255))
        {
            return 4;
        }

        return stroke == new ColorRgba(76, 194, 255, 255)
            ? 0
            : AnnotationStrokeCustomIndex;
    }

    private void NotifyAnnotationStyleControlsChanged()
    {
        NotifyPropertyContextChanged();
    }

    private static string ResolveDocumentTitle(CaptureDocument document)
    {
        return document.Metadata.Properties.TryGetValue("title", out string? title)
            ? title
            : $"Capture {document.Metadata.CreatedAtUtc.LocalDateTime:g}";
    }

    private static string CreateDocumentDetail(CaptureDocument document)
    {
        string annotationLabel = document.Annotations.Count == 1
            ? "1 annotation"
            : $"{document.Annotations.Count} annotations";

        return $"{document.SourceImage.Width} x {document.SourceImage.Height} - {annotationLabel}";
    }

    private void UpdateCurrentDocumentProperties(CaptureDocument? document)
    {
        OnPropertyChanged(nameof(CurrentCaptureTagsLabel));
        if (document is null)
        {
            CurrentDocumentWidthLabel = "-";
            CurrentDocumentHeightLabel = "-";
            CurrentDocumentFileSizeLabel = "-";
            CurrentDocumentFileNameLabel = "-";
            CurrentDocumentLocationLabel = "-";
            CurrentDocumentFooterLabel = "No capture selected.";
            OnPropertyChanged(nameof(CurrentDocumentSizeLabel));
            return;
        }

        CurrentDocumentWidthLabel = string.Format(
            CultureInfo.InvariantCulture,
            "{0} px",
            document.SourceImage.Width);
        CurrentDocumentHeightLabel = string.Format(
            CultureInfo.InvariantCulture,
            "{0} px",
            document.SourceImage.Height);
        CurrentDocumentFileSizeLabel = CreateImageFileSizeLabel(document.SourceImage.Path);
        CurrentDocumentFileNameLabel = CreateImageFileNameLabel(document.SourceImage.Path);
        CurrentDocumentLocationLabel = CreateImageLocationLabel(document.SourceImage.Path);
        CurrentDocumentFooterLabel = CreateCurrentDocumentFooterLabel(document);
        OnPropertyChanged(nameof(CurrentDocumentSizeLabel));
    }

    private string CreateCurrentDocumentFooterLabel(CaptureDocument document)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} | {1} | {2} | {3} | Created {4:g} | Modified {5:g}",
            CurrentDocumentFileNameLabel,
            CurrentDocumentLocationLabel,
            CurrentDocumentSizeLabel,
            CurrentDocumentFileSizeLabel,
            document.Metadata.CreatedAtUtc.LocalDateTime,
            document.Metadata.ModifiedAtUtc.LocalDateTime);
    }

    private static string CreateImageFileNameLabel(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return "No source file";
        }

        try
        {
            string fileName = Path.GetFileName(imagePath);
            return string.IsNullOrWhiteSpace(fileName)
                ? imagePath
                : fileName;
        }
        catch (ArgumentException)
        {
            return imagePath;
        }
    }

    private static string CreateImageLocationLabel(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return "No location";
        }

        try
        {
            string? directory = Path.GetDirectoryName(imagePath);
            return string.IsNullOrWhiteSpace(directory)
                ? imagePath
                : directory;
        }
        catch (ArgumentException)
        {
            return imagePath;
        }
    }

    private static string CreateImageFileSizeLabel(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return "Unavailable";
        }

        try
        {
            return FormatByteSize(new FileInfo(imagePath).Length);
        }
        catch (IOException)
        {
            return "Unavailable";
        }
        catch (UnauthorizedAccessException)
        {
            return "Unavailable";
        }
    }

    private static string FormatByteSize(long byteCount)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0, byteCount);
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", byteCount, units[unitIndex])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", value, units[unitIndex]);
    }

    private string CreateExportFileName(ExportFormat format)
    {
        string extension = format == ExportFormat.Jpeg ? "jpg" : format.ToString().ToLowerInvariant();
        string title = string.Join(
            "_",
            CurrentDocumentTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(title)
            ? $"capture.{extension}"
            : $"{title}.{extension}";
    }

    private static string CreateDuplicateTitle(string title)
    {
        string normalized = string.IsNullOrWhiteSpace(title)
            ? "Untitled Capture"
            : title.Trim();

        return normalized.StartsWith("Copy of ", StringComparison.OrdinalIgnoreCase)
            ? $"{normalized} copy"
            : $"Copy of {normalized}";
    }

    private readonly record struct ResizeDimensions(double Width, double Height);

    private void SetStatus(string status)
    {
        if (_dispatcherQueue is null)
        {
            StatusText = status;
            return;
        }

        bool enqueued = _dispatcherQueue.TryEnqueue(() => StatusText = status);
        if (!enqueued)
        {
            StatusText = status;
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
