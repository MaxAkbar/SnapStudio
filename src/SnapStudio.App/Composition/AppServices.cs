using SnapStudio.Core.Capture;
using SnapStudio.Core.Diagnostics;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Export;
using SnapStudio.Core.Messaging;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Rendering;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.Settings;
using SnapStudio.Core.System;
using SnapStudio.Ipc;
using SnapStudio.App.Capture;
using SnapStudio.App.Workspace;
using SnapStudio.Platform.Windows;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.App.Composition;

public sealed record AppServices(
    ISettingsStore SettingsStore,
    ISettingsImportExportService SettingsImportExportService,
    ISettingsFilePicker SettingsFilePicker,
    IDocumentCatalog DocumentCatalog,
    IDocumentRepository DocumentRepository,
    IDocumentThumbnailCache DocumentThumbnailCache,
    IDocumentWorkspaceBootstrapper DocumentWorkspaceBootstrapper,
    IEditCommandStack EditCommandStack,
    IDocumentRenderer DocumentRenderer,
    IDocumentRasterEditor DocumentRasterEditor,
    ICaptureTargetSelector CaptureTargetSelector,
    ICaptureWorkflow CaptureWorkflow,
    IImageImportService ImageImportService,
    IClipboardService ClipboardService,
    IExportDestinationPicker ExportDestinationPicker,
    IReadOnlyDictionary<ExportFormat, IExportProvider> ExportProviders,
    IHotkeyService HotkeyService,
    IWorkspaceShellService WorkspaceShellService,
    IFileTrashService FileTrashService,
    IPinnedImageService PinnedImageService,
    IStorageLocationPicker StorageLocationPicker,
    IDiagnosticLog DiagnosticLog,
    ICrashRecoveryJournal CrashRecoveryJournal,
    string RecoverySessionId,
    IFeatureFlagService FeatureFlags,
    IOcrProvider OcrProvider,
    IOcrResultCache OcrResultCache,
    IOcrTextExtractionService OcrTextExtractionService,
    IScrollTargetDetector ScrollTargetDetector,
    IScrollInputController ScrollInputController,
    IScrollingFrameCaptureService ScrollingFrameCaptureService,
    IScrollingCaptureService ScrollingCaptureService,
    IScrollingStitcher ScrollingStitcher,
    IScreenRecordingService ScreenRecordingService,
    NamedPipeEditorMessageServer EditorMessageServer)
{
    public static AppServices CreateDefault(
        nint ownerWindowHandle = 0,
        IRegionSelectionService? regionSelection = null,
        ICrashRecoveryJournal? crashRecoveryJournal = null,
        string? recoverySessionId = null)
    {
        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapStudio");

        var defaultSettings = ApplicationSettings.CreateDefault(Path.Combine(appDataRoot, "Documents"));
        var settingsStore = new JsonSettingsStore(Path.Combine(appDataRoot, "settings.json"), defaultSettings);
        var settingsImportExportService = new JsonSettingsImportExportService(defaultSettings);
        ApplicationSettings settings = settingsStore
            .LoadAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        string storageRoot = settings.StorageRoot;

        (IDocumentRepository documentRepository, IDocumentCatalog documentCatalog) =
            CreateDocumentStorage(settings);
        var diagnosticLog = new FileDiagnosticLog(Path.Combine(appDataRoot, "Logs", "snapstudio.jsonl"));
        crashRecoveryJournal ??= CreateCrashRecoveryJournal(appDataRoot);
        recoverySessionId = string.IsNullOrWhiteSpace(recoverySessionId)
            ? Guid.NewGuid().ToString("N")
            : recoverySessionId;
        var featureFlags = new InMemoryFeatureFlagService(settings.FeatureFlags);
        var workspaceBootstrapper = new DocumentWorkspaceBootstrapper(
            storageRoot,
            documentRepository,
            documentCatalog);
        var captureItemRegistry = new WindowsGraphicsCaptureItemRegistry();
        var captureCapabilities = new WindowsGraphicsCaptureCapabilityService();
        ICaptureTargetSelector captureTargetSelector = CreateCaptureTargetSelector(
            ownerWindowHandle,
            captureItemRegistry,
            regionSelection);
        IStillCaptureService stillCapture = new WindowsGraphicsCaptureStillCaptureService(
            captureItemRegistry,
            Path.Combine(storageRoot, "SourceImages"),
            captureCapabilities);
        var captureWorkflow = new CaptureWorkflow(
            captureTargetSelector,
            stillCapture,
            documentRepository,
            new NoOpEditorMessageClient());
        var imageImportService = new WindowsImageImportService(
            ownerWindowHandle,
            Path.Combine(storageRoot, "SourceImages"));
        var documentRenderer = new SystemDrawingDocumentRenderer(documentRepository);
        var documentThumbnailCache = new FileSystemDocumentThumbnailCache(
            storageRoot,
            documentRepository,
            documentRenderer);
        IOcrProvider ocrProvider = new WindowsOcrProviderFactory().Create();
        var ocrResultCache = new FileSystemOcrResultCache(storageRoot);
        var ocrTextExtractionService = new OcrTextExtractionService(ocrProvider, ocrResultCache);
        var documentRasterEditor = new SystemDrawingDocumentRasterEditor();
        var clipboardService = new WindowsClipboardService(documentRenderer);
        IScrollTargetDetector scrollTargetDetector = featureFlags.IsEnabled("V1.ScrollingCapture")
            ? new WindowsAutomationScrollTargetDetector()
            : new UnsupportedScrollTargetDetector();
        IScrollInputController scrollInputController = featureFlags.IsEnabled("V1.ScrollingCapture")
            ? new WindowsAutomationScrollInputController()
            : new UnsupportedScrollInputController();
        IScrollingFrameCaptureService scrollingFrameCaptureService = featureFlags.IsEnabled("V1.ScrollingCapture")
            ? new WindowsBoundsScrollingFrameCaptureService()
            : new UnsupportedScrollingFrameCaptureService();
        IScrollingStitcher scrollingStitcher = featureFlags.IsEnabled("V1.ScrollingCapture")
            ? new SystemDrawingVerticalOverlapScrollingStitcher()
            : new UnsupportedScrollingStitcher();
        IScrollingCaptureService scrollingCaptureService = featureFlags.IsEnabled("V1.ScrollingCapture")
            ? new ScrollingCaptureOrchestrator(
                scrollingFrameCaptureService,
                scrollInputController,
                scrollingStitcher,
                new FileSystemScrollingCaptureDiagnosticsBundleWriter())
            : new UnsupportedScrollingCaptureService();
        IScreenRecordingService screenRecordingService = featureFlags.IsEnabled("V1.ScreenRecording")
            ? new ScreenRecordingSessionCoordinator(new ScreenRecordingPipelineEngine(
                new WindowsGraphicsCaptureScreenRecordingFrameSource(
                    captureItemRegistry,
                    captureCapabilities),
                new WindowsMp4ScreenRecordingOutputWriter(),
                audioSource: new WindowsWasapiScreenRecordingAudioSource()))
            : new UnsupportedScreenRecordingService();
        IHotkeyService hotkeyService = ownerWindowHandle == 0
            ? new UnsupportedHotkeyService()
            : new WindowsHotkeyService(ownerWindowHandle);
        var exportProviders = new Dictionary<ExportFormat, IExportProvider>
        {
            [ExportFormat.Png] = new RenderedDocumentExportProvider(ExportFormat.Png, documentRenderer),
            [ExportFormat.Jpeg] = new RenderedDocumentExportProvider(ExportFormat.Jpeg, documentRenderer),
            [ExportFormat.Pdf] = new PdfDocumentExportProvider(documentRenderer)
        };

        return new AppServices(
            settingsStore,
            settingsImportExportService,
            new WindowsSettingsFilePicker(ownerWindowHandle),
            documentCatalog,
            documentRepository,
            documentThumbnailCache,
            workspaceBootstrapper,
            new EditCommandStack(),
            documentRenderer,
            documentRasterEditor,
            captureTargetSelector,
            captureWorkflow,
            imageImportService,
            clipboardService,
            new WindowsFileSaveExportDestinationPicker(ownerWindowHandle),
            exportProviders,
            hotkeyService,
            new WindowsWorkspaceShellService(),
            new WindowsFileTrashService(),
            new PinnedImageService(documentRenderer),
            new WindowsStorageLocationPicker(ownerWindowHandle),
            diagnosticLog,
            crashRecoveryJournal,
            recoverySessionId,
            featureFlags,
            ocrProvider,
            ocrResultCache,
            ocrTextExtractionService,
            scrollTargetDetector,
            scrollInputController,
            scrollingFrameCaptureService,
            scrollingCaptureService,
            scrollingStitcher,
            screenRecordingService,
            new NamedPipeEditorMessageServer(NamedPipeEditorMessageDefaults.PipeName));
    }

    public static ICrashRecoveryJournal CreateCrashRecoveryJournal(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        return new FileCrashRecoveryJournal(Path.Combine(appDataRoot, "Logs", "recovery.jsonl"));
    }

    private static (IDocumentRepository Repository, IDocumentCatalog Catalog) CreateDocumentStorage(
        ApplicationSettings settings)
    {
        return settings.StorageBackend switch
        {
            ApplicationStorageBackend.Database => CreateDatabaseDocumentStorage(settings.StorageRoot),
            _ => (
                new FileSystemDocumentRepository(settings.StorageRoot),
                new FileSystemDocumentCatalog(settings.StorageRoot))
        };
    }

    private static (IDocumentRepository Repository, IDocumentCatalog Catalog) CreateDatabaseDocumentStorage(
        string storageRoot)
    {
        var store = new CSharpDbDocumentStore(storageRoot);
        return (
            new CSharpDbDocumentRepository(store),
            new CSharpDbDocumentCatalog(store));
    }

    private static ICaptureTargetSelector CreateCaptureTargetSelector(
        nint ownerWindowHandle,
        WindowsGraphicsCaptureItemRegistry captureItemRegistry,
        IRegionSelectionService? regionSelection)
    {
        if (ownerWindowHandle == 0)
        {
            return new UnavailableCaptureTargetSelector();
        }

        var selectors = new List<ICaptureTargetSelector>();
        if (regionSelection is not null)
        {
            selectors.Add(new WindowsRegionCaptureTargetSelector(
                regionSelection,
                captureItemRegistry));
        }

        selectors.Add(new WindowsDirectDisplayCaptureTargetSelector(captureItemRegistry));
        selectors.Add(new WindowsDirectWindowCaptureTargetSelector(
            ownerWindowHandle,
            captureItemRegistry));
        selectors.Add(new WindowsGraphicsCapturePickerTargetSelector(
            ownerWindowHandle,
            captureItemRegistry));

        return new CompositeCaptureTargetSelector(selectors);
    }
}
