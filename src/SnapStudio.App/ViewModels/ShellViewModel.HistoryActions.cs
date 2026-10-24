using Microsoft.UI.Xaml.Controls;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    public bool CanManageCaptureHistory => _documentRepository is not null
        && !_captureInProgress && !_historyFolderOperationInProgress
        && !_screenRecordingOperationInProgress && !IsScreenRecording && !_fillInProgress;

    public bool CanRecycleCaptureImage(string documentId)
    {
        if (!CanManageCaptureHistory || _fileTrashService is null
            || !Guid.TryParse(documentId, out Guid value)) return false;
        var id = new DocumentId(value);
        string? path = _currentDocumentId == id ? _currentDocument?.SourceImage.Path
            : _allRecentDocuments.FirstOrDefault(document => document.Id == id)?.SourceImagePath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public Task RemoveCaptureFromHistoryAsync(string documentId, CancellationToken cancellationToken) =>
        Guid.TryParse(documentId, out Guid value)
            ? RunCaptureHistoryActionAsync(() => DeleteDocumentRecordAsync(new DocumentId(value),
                "Removed capture from recent captures. Its image file was kept.", cancellationToken), cancellationToken)
            : Task.CompletedTask;

    public Task MoveCaptureToTrashAsync(string documentId, CancellationToken cancellationToken) =>
        Guid.TryParse(documentId, out Guid value)
            ? RunCaptureHistoryActionAsync(() => MoveCaptureToTrashCoreAsync(new DocumentId(value), cancellationToken), cancellationToken)
            : Task.CompletedTask;

    public void ShowCaptureHistoryError(string message)
    {
        StatusText = message;
        ShowCaptureNotice("Capture could not be removed", message, InfoBarSeverity.Error);
    }

    private async Task RunCaptureHistoryActionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!CanManageCaptureHistory) return;
        SetCaptureInProgress(true);
        try
        {
            await _pendingDocumentSave.WaitAsync(cancellationToken).ConfigureAwait(true);
            await action().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowCaptureHistoryError(exception.Message);
        }
        finally { SetCaptureInProgress(false); }
    }
}
