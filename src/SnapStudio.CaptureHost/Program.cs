using SnapStudio.Core.Capture;
using SnapStudio.Core.Messaging;
using SnapStudio.Ipc;
using SnapStudio.Platform.Windows;

IStillCaptureCapabilityService captureCapabilities = new WindowsGraphicsCaptureCapabilityService();
StillCaptureCapability capability = await captureCapabilities.GetCapabilityAsync(CancellationToken.None);

Console.WriteLine("SnapStudio capture host");
Console.WriteLine(capability.IsSupported
    ? "Windows.Graphics.Capture is available."
    : capability.UnavailableReason);

if (!args.Contains("--capture-full-screen", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Probe complete. Pass --capture-full-screen to acquire a host source image.");
    return;
}

if (!capability.IsSupported)
{
    Console.WriteLine(capability.UnavailableReason);
    return;
}

var registry = new WindowsGraphicsCaptureItemRegistry();
var targetSelector = new WindowsDirectDisplayCaptureTargetSelector(registry);
var captureService = new WindowsGraphicsCaptureStillCaptureService(
    registry,
    capabilities: captureCapabilities);
IEditorMessageClient editorMessages = new NamedPipeEditorMessageClient(
    NamedPipeEditorMessageDefaults.PipeName,
    connectionTimeout: TimeSpan.FromMilliseconds(500));

CaptureTargetSelection? selection = await targetSelector.SelectTargetAsync(
    new CaptureTargetRequest(
        [CaptureTargetKind.FullScreen],
        AllowDelayedCapture: false),
    CancellationToken.None);

if (selection is null)
{
    await ReportFailureAsync(
        editorMessages,
        new CaptureFailure(
            CaptureFailureReason.TargetUnavailable,
            "No full-screen capture target was available."),
        CancellationToken.None);
    return;
}

CaptureOutcome outcome = await captureService.CaptureAsync(
    new CaptureRequest(
        selection.TargetKind,
        IncludeCursor: true,
        Delay: TimeSpan.Zero,
        selection.TargetId,
        selection.Bounds,
        selection.Metadata),
    CancellationToken.None);

if (outcome.Succeeded && outcome.Capture is not null)
{
    Console.WriteLine($"Source image captured: {outcome.Capture.SourceImage.Path}");
    Console.WriteLine("Editor document persistence is handled by the editor workflow; host handoff is deferred.");
    return;
}

await ReportFailureAsync(
    editorMessages,
    outcome.Failure ?? new CaptureFailure(CaptureFailureReason.Unknown, "Capture failed."),
    CancellationToken.None);

static async Task ReportFailureAsync(
    IEditorMessageClient editorMessages,
    CaptureFailure failure,
    CancellationToken cancellationToken)
{
    Console.WriteLine(failure.Message);

    EditorMessageSendResult notification = await editorMessages.SendAsync(
        new CaptureFailedEditorMessage(failure.Reason, failure.Message),
        cancellationToken);

    if (!notification.Succeeded)
    {
        Console.WriteLine($"Editor notification skipped: {notification.ErrorMessage}");
    }
}
