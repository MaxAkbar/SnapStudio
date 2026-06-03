using SnapStudio.Core.Capture;
using SnapStudio.Core.Export;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsPlatformPlaceholderTests
{
    [Fact]
    public async Task UnavailableCaptureTargetSelector_ReturnsNoSelection()
    {
        var selector = new UnavailableCaptureTargetSelector();

        CaptureTargetSelection? selection = await selector.SelectTargetAsync(
            new CaptureTargetRequest([CaptureTargetKind.FullScreen], AllowDelayedCapture: false),
            CancellationToken.None);

        Assert.Null(selection);
    }

    [Fact]
    public async Task UnavailableStillCaptureService_ReturnsTypedFailure()
    {
        var capture = new UnavailableStillCaptureService();

        CaptureOutcome outcome = await capture.CaptureAsync(
            new CaptureRequest(CaptureTargetKind.FullScreen, IncludeCursor: true, Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(CaptureFailureReason.NotImplemented, outcome.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedClipboardService_ReturnsTypedFailure()
    {
        var clipboard = new UnsupportedClipboardService();

        ClipboardResult result = await clipboard.CopyDocumentAsync(
            DocumentId.New(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("deferred", result.ErrorMessage);
    }

    [Fact]
    public async Task UnsupportedClipboardService_ReturnsTypedFailureForText()
    {
        var clipboard = new UnsupportedClipboardService();

        ClipboardResult result = await clipboard.CopyTextAsync(
            "recognized text",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task UnsupportedExportProvider_ReturnsTypedFailureForFormat()
    {
        var export = new UnsupportedExportProvider(ExportFormat.Png);

        ExportResult result = await export.ExportAsync(
            new ExportRequest(DocumentId.New(), ExportFormat.Png, "capture.png", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.OutputPath);
        Assert.Contains("Png export", result.ErrorMessage);
    }

    [Fact]
    public async Task UnsupportedHotkeyService_ReturnsTypedFailure()
    {
        var hotkeys = new UnsupportedHotkeyService();

        HotkeyRegistrationResult result = await hotkeys.RegisterAsync(
            new HotkeyRegistration("Capture", "PrintScreen"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("deferred", result.ErrorMessage);
    }

    [Fact]
    public async Task UnavailableOcrProvider_ReturnsUnavailableStatusAndTypedFailure()
    {
        var provider = new UnavailableOcrProvider("Packaged build required.");

        OcrProviderStatus status = await provider.GetStatusAsync(CancellationToken.None);
        OcrRecognitionResult result = await provider.RecognizeAsync(
            new OcrRequest(
                DocumentId.New(),
                new ImageAsset("source.png", 100, 50, ImagePixelFormat.Bgra32),
                new RectD(0, 0, 100, 50),
                "en-US"),
            CancellationToken.None);

        Assert.False(status.IsAvailable);
        Assert.Equal("Windows OCR", status.ProviderName);
        Assert.Contains("Packaged", status.UnavailableReason);
        Assert.False(result.Succeeded);
        Assert.Equal(OcrFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScrollTargetDetector_ReturnsNoTargets()
    {
        var detector = new UnsupportedScrollTargetDetector();

        IReadOnlyList<ScrollTargetCandidate> targets = await detector.DetectAsync(
            new ScrollTargetDetectionRequest(),
            CancellationToken.None);

        Assert.Empty(targets);
    }

    [Fact]
    public async Task UnsupportedScrollingCaptureService_ReturnsTypedFailure()
    {
        var service = new UnsupportedScrollingCaptureService();

        ScrollingCaptureResult result = await service.CaptureAsync(
            new ScrollingCaptureRequest(
                CreateScrollTarget(),
                Path.GetTempPath()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollingCaptureFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScrollInputController_ReturnsTypedFailure()
    {
        var controller = new UnsupportedScrollInputController();

        ScrollInputResult result = await controller.ScrollAsync(
            new ScrollInputRequest(
                CreateScrollTarget(),
                ScrollInputDirection.Down),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollInputFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScrollingFrameCaptureService_ReturnsTypedFailure()
    {
        var service = new UnsupportedScrollingFrameCaptureService();

        ScrollingFrameCaptureResult result = await service.CaptureFrameAsync(
            new ScrollingFrameCaptureRequest(
                CreateScrollTarget(),
                0,
                Path.GetTempPath()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollingCaptureFailureReason.FrameCaptureFailed, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScrollingStitcher_ReturnsTypedFailure()
    {
        var stitcher = new UnsupportedScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest(
                [],
                Path.GetTempPath(),
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScrollingCaptureFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScreenRecordingService_ReturnsTypedFailure()
    {
        var service = new UnsupportedScreenRecordingService();

        ScreenRecordingStartResult result = await service.StartAsync(
            new ScreenRecordingStartRequest(
                CaptureTargetKind.Display,
                Path.Combine(Path.GetTempPath(), "recording.mp4")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScreenRecordingEngine_ReturnsTypedFailure()
    {
        var engine = new UnsupportedScreenRecordingEngine();

        ScreenRecordingStartResult result = await engine.StartAsync(
            new ScreenRecordingStartRequest(
                CaptureTargetKind.Display,
                Path.Combine(Path.GetTempPath(), "recording.mp4")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScreenRecordingFrameSource_ReturnsTypedFailure()
    {
        var frameSource = new UnsupportedScreenRecordingFrameSource();

        ScreenRecordingFrameSourceOpenResult result = await frameSource.OpenAsync(
            new ScreenRecordingFrameSourceOpenRequest(
                ScreenRecordingSessionId.New(),
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    Path.Combine(Path.GetTempPath(), "recording.mp4"))),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.Unsupported, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScreenRecordingAudioSource_ReturnsAudioUnavailable()
    {
        var audioSource = new UnsupportedScreenRecordingAudioSource();

        ScreenRecordingAudioSourceOpenResult result = await audioSource.OpenAsync(
            new ScreenRecordingAudioSourceOpenRequest(
                ScreenRecordingSessionId.New(),
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    Path.Combine(Path.GetTempPath(), "recording.mp4"),
                    IncludeMicrophoneAudio: true)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.AudioUnavailable, result.Failure?.Reason);
    }

    [Fact]
    public async Task UnsupportedScreenRecordingOutputWriter_ReturnsTypedFailure()
    {
        var writer = new UnsupportedScreenRecordingOutputWriter();

        ScreenRecordingOutputWriterStartResult result = await writer.StartAsync(
            new ScreenRecordingOutputWriterStartRequest(
                ScreenRecordingSessionId.New(),
                new ScreenRecordingStartRequest(
                    CaptureTargetKind.Display,
                    Path.Combine(Path.GetTempPath(), "recording.mp4")),
                new ScreenRecordingFrameSourceSession(
                    ScreenRecordingSessionId.New(),
                    1280,
                    720,
                    "Bgra32",
                    TimeSpan.FromMilliseconds(16.667),
                    new Dictionary<string, string>())),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.EncoderUnavailable, result.Failure?.Reason);
    }

    [Fact]
    public async Task WindowsImageImportService_WhenOwnerWindowIsMissing_ReturnsUnsupported()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var import = new WindowsImageImportService(0, workspace.Path);

        CaptureOutcome outcome = await import.ImportAsync(CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(CaptureFailureReason.Unsupported, outcome.Failure?.Reason);
    }

    [Fact]
    public async Task WindowsStorageLocationPicker_WhenOwnerWindowIsMissing_ReturnsNull()
    {
        var picker = new WindowsStorageLocationPicker(0);

        string? selectedPath = await picker.PickStorageRootAsync(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            CancellationToken.None);

        Assert.Null(selectedPath);
    }

    [Fact]
    public async Task WindowsFileTrashService_WhenFileIsMissing_ReturnsFailed()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var trash = new WindowsFileTrashService();

        FileTrashResult result = await trash.MoveFileToTrashAsync(
            Path.Combine(workspace.Path, "missing.png"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    private static ScrollTargetCandidate CreateScrollTarget()
    {
        return new ScrollTargetCandidate(
            "target-1",
            "Scrollable Window",
            ScrollTargetKind.Window,
            new RectD(0, 0, 800, 600),
            new Dictionary<string, string>());
    }
}
