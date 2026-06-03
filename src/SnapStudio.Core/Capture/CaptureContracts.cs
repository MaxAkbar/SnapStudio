using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Capture;

public interface IStillCaptureService
{
    Task<CaptureOutcome> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
}

public interface IStillCaptureCapabilityService
{
    Task<StillCaptureCapability> GetCapabilityAsync(CancellationToken cancellationToken);
}

public interface ICaptureTargetSelector
{
    Task<CaptureTargetSelection?> SelectTargetAsync(
        CaptureTargetRequest request,
        CancellationToken cancellationToken);
}

public interface IRegionSelectionService
{
    Task<RectD?> SelectRegionAsync(
        RectD virtualScreenBounds,
        CancellationToken cancellationToken);
}

public interface IImageImportService
{
    Task<CaptureOutcome> ImportAsync(CancellationToken cancellationToken);
}

public interface IScreenPreviewService
{
    Task<ScreenPreviewImage?> CapturePreviewAsync(
        RectD virtualScreenBounds,
        CancellationToken cancellationToken);
}

public interface ICaptureWorkflow
{
    Task<CaptureWorkflowResult> CaptureAsync(
        CaptureWorkflowRequest request,
        CancellationToken cancellationToken);
}
