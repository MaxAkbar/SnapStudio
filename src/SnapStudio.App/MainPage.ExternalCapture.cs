using SnapStudio.App.Composition;
using SnapStudio.Core.Capture;
using SnapStudio.Ipc;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private readonly CancellationTokenSource _externalCaptureLifetime = new();
    private Task? _externalCaptureTask;
    public void StopExternalCapture() => _externalCaptureLifetime.Cancel();
    private void StartExternalCapture(AppServices services)
    {
        if (_externalCaptureTask is not null || services.ExternalCaptureFactory is null) return;
        _externalCaptureTask = ListenAsync();
        async Task ListenAsync()
        {
            try
            {
                await new ExternalCaptureServer(ExternalCaptureServer.DefaultPipeName).RunAsync(DispatchAsync, _externalCaptureLifetime.Token);
            }
            catch (OperationCanceledException) when (_externalCaptureLifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                DispatcherQueue.TryEnqueue(() => ViewModel.ShowCaptureHistoryError("ExactFrame connection unavailable: " + error.Message));
            }
        }
        Task<ExternalCaptureResponse> DispatchAsync(ExternalCaptureRequest request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<ExternalCaptureResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!_isReadyForControlEvents || _canvasDragMode != CanvasDragMode.None || ViewModel.FirstRunSetupIsOpen
                        || _newImageDialogIsOpen || _pasteImageDialogIsOpen || _layerRenameDialogIsOpen || _layerColorDialogIsOpen
                        || _historyFolderDialogIsOpen || _captureDeleteDialogIsOpen || _captureRenameDialogIsOpen || _captureTagsDialogIsOpen
                        || _toolDesignDialogIsOpen || _selectionFillDialogIsOpen || _stampDialogIsOpen)
                        completion.TrySetResult(new(1, request.RequestId, false, "Finish the current SnapStudio dialog or edit, then try again."));
                    else
                    {
                        var result = await ViewModel.CaptureExternalRegionAsync(request, services.ExternalCaptureFactory(request.Bounds), token);
                        if (result.Succeeded) { FitCurrentCanvas(); services.ActivateWorkspace?.Invoke(); }
                        completion.TrySetResult(result);
                    }
                }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new OperationCanceledException("SnapStudio is closing."));
            return completion.Task.WaitAsync(token);
        }
    }
}
