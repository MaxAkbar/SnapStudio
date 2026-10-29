using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    public async Task<ExternalCaptureResponse> CaptureExternalRegionAsync(ExternalCaptureRequest request, ICaptureWorkflow workflow, CancellationToken token)
    {
        request.Validate();
        if (_documentRepository is null || _documentCatalog is null || _featureFlags?.IsEnabled(StillCaptureFeatureFlag) != true)
            return new(1, request.RequestId, false, "Still capture is unavailable or disabled in SnapStudio.");
        if (_captureInProgress || _activeScreenRecordingSession is not null || _screenRecordingOperationInProgress)
            return new(1, request.RequestId, false, "SnapStudio is already capturing or recording. Try again when it finishes.");
        SetCaptureInProgress(true);
        try
        {
            var result = await workflow.CaptureAsync(new([CaptureTargetKind.Region], false, TimeSpan.Zero), token);
            if (!result.Succeeded || result.DocumentId is not DocumentId id)
                return new(1, request.RequestId, false, result.Failure?.Message ?? "The frame could not be captured.");
            var document = await _documentRepository.GetAsync(id, token) ?? throw new InvalidDataException("The captured document could not be opened.");
            document.Metadata.Properties["title"] = string.IsNullOrWhiteSpace(request.Name) ? "ExactFrame capture" : "ExactFrame - " + request.Name;
            document.Metadata.Properties["source"] = "ExactFrame";
            await _documentRepository.SaveAsync(document, token);
            await RefreshRecentDocumentsAsync(token);
            await OpenDocumentAsync(id, token);
            StatusText = "ExactFrame capture opened. Add annotations, then copy or export.";
            return new(1, request.RequestId, true, null, id.Value);
        }
        finally { SetCaptureInProgress(false); }
    }
}
