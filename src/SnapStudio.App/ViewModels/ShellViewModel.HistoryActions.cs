using Microsoft.UI.Xaml.Controls;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;

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
        string? path = GetHistoryCaptureImagePath(new DocumentId(value));
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public bool CanShowCaptureInFolder(string documentId) => CanManageCaptureHistory
        && _workspaceShellService is not null && Guid.TryParse(documentId, out Guid value)
        && !string.IsNullOrWhiteSpace(GetHistoryCaptureImagePath(new DocumentId(value)));

    public Task RenameCaptureAsync(string documentId, string title, CancellationToken cancellationToken) =>
        Guid.TryParse(documentId, out Guid value)
            ? RunCaptureHistoryActionAsync(async () =>
            {
                CaptureDocument? document = await GetHistoryCaptureAsync(new DocumentId(value), cancellationToken).ConfigureAwait(true);
                if (document is null) return;
                CaptureDocumentWorkspaceEditor.SetTitle(document, title);
                if (_currentDocumentId == document.Id)
                {
                    CurrentDocumentTitle = ResolveDocumentTitle(document);
                    WorkspaceTitleText = CurrentDocumentTitle;
                    await SaveCurrentDocumentAsync("Renamed capture.", cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _documentRepository!.SaveAsync(document, cancellationToken).ConfigureAwait(true);
                    await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
                    StatusText = "Renamed capture.";
                }
            }, cancellationToken) : Task.CompletedTask;

    public Task DuplicateCaptureAsync(string documentId, CancellationToken cancellationToken) =>
        Guid.TryParse(documentId, out Guid value)
            ? RunCaptureHistoryActionAsync(async () =>
            {
                CaptureDocument? source = await GetHistoryCaptureAsync(new DocumentId(value), cancellationToken).ConfigureAwait(true);
                if (source is null) return;
                CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(source, CreateDuplicateTitle(ResolveDocumentTitle(source)));
                await _documentRepository!.SaveAsync(duplicate, cancellationToken).ConfigureAwait(true);
                string? folderError = null;
                if (_historyFolderStore is not null && _historyFoldersLoaded && _historyFolders.GetFolderId(source.Id) is Guid folderId)
                {
                    try
                    {
                        HistoryFolderState next = _historyFolders.Copy();
                        next.MoveDocument(duplicate.Id, folderId);
                        await SaveHistoryFoldersAsync(next, cancellationToken).ConfigureAwait(true);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException) { folderError = exception.Message; }
                }
                await RefreshRecentDocumentsAsync(cancellationToken).ConfigureAwait(true);
                StatusText = folderError is null ? "Duplicated capture." : "Capture duplicated, but its folder could not be saved. The copy is in Unfiled.";
                if (folderError is not null) ShowCaptureNotice("Capture duplicated", $"{StatusText}\n{folderError}", InfoBarSeverity.Warning);
            }, cancellationToken) : Task.CompletedTask;

    public Task ShowCaptureInFolderAsync(string documentId, CancellationToken cancellationToken) =>
        _workspaceShellService is not null && Guid.TryParse(documentId, out Guid value)
            ? RunCaptureHistoryActionAsync(async () =>
            {
                CaptureDocument? document = await GetHistoryCaptureAsync(new DocumentId(value), cancellationToken).ConfigureAwait(true);
                if (document is null) return;
                WorkspaceShellResult result = await _workspaceShellService.OpenContainingFolderAsync(document.SourceImage.Path, cancellationToken).ConfigureAwait(true);
                StatusText = result.Succeeded ? "Opened containing folder." : result.ErrorMessage ?? "The containing folder could not be opened.";
                if (!result.Succeeded) ShowCaptureNotice("Folder unavailable", StatusText, InfoBarSeverity.Warning);
            }, cancellationToken) : Task.CompletedTask;

    private string? GetHistoryCaptureImagePath(DocumentId id) => _currentDocumentId == id
        ? _currentDocument?.SourceImage.Path : _allRecentDocuments.FirstOrDefault(document => document.Id == id)?.SourceImagePath;

    private async Task<CaptureDocument?> GetHistoryCaptureAsync(DocumentId id, CancellationToken cancellationToken)
    {
        CaptureDocument? document = _currentDocumentId == id ? _currentDocument
            : await _documentRepository!.GetAsync(id, cancellationToken).ConfigureAwait(true);
        if (document is null) StatusText = "The capture is no longer available.";
        return document;
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
        ShowCaptureNotice("Capture action failed", message, InfoBarSeverity.Error);
    }

    private async Task RunCaptureHistoryActionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!CanManageCaptureHistory) return;
        SetCaptureInProgress(true);
        CaptureNoticeIsOpen = false;
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
