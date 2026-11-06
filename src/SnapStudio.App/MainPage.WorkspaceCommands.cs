using SnapStudio.App.Composition;
using SnapStudio.Core.Diagnostics;
using SnapStudio.Core.Integration;
using SnapStudio.Ipc;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private Task? _workspaceCommandTask;
    private bool _workspaceCommandInProgress;

    private bool ExternalWorkspaceIsBlocked => !_isReadyForControlEvents || _workspaceCommandInProgress
        || _canvasDragMode != CanvasDragMode.None || ViewModel.FirstRunSetupIsOpen
        || _newImageDialogIsOpen || _pasteImageDialogIsOpen || _layerRenameDialogIsOpen || _layerColorDialogIsOpen
        || _historyFolderDialogIsOpen || _captureDeleteDialogIsOpen || _captureRenameDialogIsOpen || _captureTagsDialogIsOpen
        || _toolDesignDialogIsOpen || _selectionFillDialogIsOpen || _stampDialogIsOpen;

    private void StartWorkspaceCommands(AppServices services)
    {
        if (_workspaceCommandTask is not null) return;
        var dispatcherQueue = DispatcherQueue;
        _workspaceCommandTask = Task.Run(ListenAsync);

        async Task ListenAsync()
        {
            try
            {
                // Discovery is optional; a read-only profile must not affect the editor.
                if (Environment.ProcessPath is string executable) WorkspaceAppLocation.TryRemember(executable);
                await new WorkspaceCommandServer(WorkspaceCommandServer.DefaultPipeName).RunAsync(DispatchAsync, _externalCaptureLifetime.Token);
            }
            catch (OperationCanceledException) when (_externalCaptureLifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                // An unavailable companion channel must never show an error in the normal workspace.
                try
                {
                    await services.DiagnosticLog.WriteAsync(DiagnosticEvent.Create(DiagnosticSeverity.Warning,
                        "SnapStudio.App.WorkspaceCommands", "Optional launcher connection unavailable.",
                        new Dictionary<string, string> { ["error"] = error.Message }), CancellationToken.None);
                }
                catch (Exception) { }
            }
        }

        Task<WorkspaceCommandResponse> DispatchAsync(WorkspaceCommandRequest request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<WorkspaceCommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (ExternalWorkspaceIsBlocked)
                    {
                        completion.TrySetResult(new(1, request.RequestId, false, "Finish the current SnapStudio dialog or edit, then try again."));
                        return;
                    }
                    _workspaceCommandInProgress = true;
                    try
                    {
                        var result = await ViewModel.ExecuteWorkspaceCommandAsync(request, token);
                        UpdateBindingsFromViewModel();
                        if (result.Succeeded) { FitCurrentCanvas(); services.ActivateWorkspace?.Invoke(); }
                        completion.TrySetResult(result);
                    }
                    finally { _workspaceCommandInProgress = false; }
                }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new OperationCanceledException("SnapStudio is closing."));
            return completion.Task.WaitAsync(token);
        }
    }
}
