using SnapStudio.Core.Integration;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    public async Task<WorkspaceCommandResponse> ExecuteWorkspaceCommandAsync(WorkspaceCommandRequest request, CancellationToken token)
    {
        request.Validate();
        if (_captureInProgress || _activeScreenRecordingSession is not null || _screenRecordingOperationInProgress
            || FirstRunSetupIsOpen || !_pendingDocumentSave.IsCompleted)
            return new(1, request.RequestId, false, "Finish setup or the current capture or recording in SnapStudio, then try again.");

        await _pendingDocumentSave.WaitAsync(token);
        var previousId = _currentDocumentId;
        switch (request.Command)
        {
            case WorkspaceCommandRequest.CaptureRegion:
                await CaptureAsync(CaptureMode.Region, token);
                break;
            case WorkspaceCommandRequest.EditClipboard:
                await PasteImageAsNewDocumentAsync(token);
                break;
            case WorkspaceCommandRequest.OpenLatest:
                SetCaptureInProgress(true);
                try
                {
                    var latest = _documentCatalog is null ? null : (await _documentCatalog.GetRecentAsync(1, token)).FirstOrDefault();
                    if (latest is null) return new(1, request.RequestId, false, "No captures yet. Capture a region or open a clipboard image first.");
                    bool opened = await OpenDocumentAsync(latest.Id, token);
                    return opened
                        ? new(1, request.RequestId, true, null, latest.Id.Value)
                        : new(1, request.RequestId, false, "The most recent image could not be opened. Check its source file in SnapStudio.");
                }
                finally { SetCaptureInProgress(false); }
        }
        return _currentDocumentId is { } id && id != previousId
            ? new(1, request.RequestId, true, null, id.Value)
            : new(1, request.RequestId, false, StatusText);
    }
}
