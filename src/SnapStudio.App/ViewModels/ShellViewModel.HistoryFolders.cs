using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly IHistoryFolderStore? _historyFolderStore;
    private HistoryFolderState _historyFolders = new();
    private Guid? _selectedHistoryFolderId;
    private bool _historyUnfiledOnly;
    private bool _refreshingHistoryFolders;
    private bool _historyFoldersLoaded;
    private bool _historyFolderOperationInProgress;
    private CancellationTokenSource? _folderImportCancellation;
    private string _folderImportProgressText = string.Empty;

    public ObservableCollection<HistoryFolderItem> HistoryFolders { get; } =
        [new(null, "All captures", 0), new(null, "Unfiled", 0, true)];
    public int HistoryFolderIndex => Math.Max(0, HistoryFolders.ToList().FindIndex(
        folder => folder.Id == _selectedHistoryFolderId && folder.IsUnfiled == _historyUnfiledOnly));
    public HistoryFolderItem? SelectedHistoryFolder => HistoryFolders.ElementAtOrDefault(HistoryFolderIndex);
    public bool CanManageHistoryFolders => _historyFolderStore is not null && _historyFoldersLoaded
        && !_historyFolderOperationInProgress && !_captureInProgress && !_screenRecordingOperationInProgress && !IsScreenRecording;
    public bool CanRefreshHistoryFolders => CanManageHistoryFolders && _historyFolders.Folders.Any(
        folder => folder.SourcePath is not null && (_selectedHistoryFolderId is null || folder.Id == _selectedHistoryFolderId));
    public string SelectedHistoryFolderLocation => SelectedHistoryFolder?.SourcePath ?? string.Empty;
    public Visibility SelectedHistoryFolderLocationVisibility => SelectedHistoryFolder?.SourcePath is not null ? Visibility.Visible : Visibility.Collapsed;
    public bool CanEditSelectedHistoryFolder => CanManageHistoryFolders && _selectedHistoryFolderId.HasValue;
    public bool CanMoveCurrentCaptureToHistoryFolder => CanManageHistoryFolders && HasCurrentDocument;
    public Visibility FolderImportProgressVisibility => _folderImportCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
    public string FolderImportProgressText => _folderImportProgressText;

    private async Task LoadHistoryFoldersAsync(CancellationToken cancellationToken)
    {
        if (_historyFolderStore is null) return;
        try
        {
            _historyFolders = await _historyFolderStore.LoadFoldersAsync(cancellationToken).ConfigureAwait(true);
            _historyFoldersLoaded = true;
        }
        catch (Exception exception)
        {
            ShowCaptureNotice("Folders unavailable", exception.Message, InfoBarSeverity.Error);
        }
        RefreshHistoryFolderItems();
    }

    public void SelectHistoryFolder(int index)
    {
        if (_refreshingHistoryFolders || index < 0 || index >= HistoryFolders.Count || !CanManageHistoryFolders) return;
        HistoryFolderItem folder = HistoryFolders[index];
        _selectedHistoryFolderId = folder.Id;
        _historyUnfiledOnly = folder.IsUnfiled;
        _historyPagination.Reset();
        ApplyRecentDocumentFilter(refreshThumbnails: true);
        NotifyHistoryFolderStateChanged();
    }

    public string ValidateHistoryFolderName(string name, bool rename = false) =>
        _historyFolders.ValidateName(name, rename ? _selectedHistoryFolderId : null);

    public string SuggestHistoryFolderName(string name)
    {
        string basis = string.IsNullOrWhiteSpace(name) ? "Images" : name.Trim();
        basis = basis[..Math.Min(basis.Length, 70)];
        for (int suffix = 1; ; suffix++)
        {
            string candidate = suffix == 1 ? basis : $"{basis} ({suffix})";
            try { return _historyFolders.ValidateName(candidate); }
            catch (ArgumentException) { }
        }
    }

    public Task CreateHistoryFolderAsync(string name, CancellationToken cancellationToken) =>
        RunHistoryFolderOperationAsync(async () =>
        {
            HistoryFolderState next = _historyFolders.Copy();
            HistoryFolder folder = next.AddFolder(name);
            await SaveHistoryFoldersAsync(next, cancellationToken).ConfigureAwait(true);
            _selectedHistoryFolderId = folder.Id;
            _historyUnfiledOnly = false;
            StatusText = $"Created folder {folder.Name}.";
        });

    public Task RenameHistoryFolderAsync(string name, CancellationToken cancellationToken) =>
        RunHistoryFolderOperationAsync(async () =>
        {
            if (_selectedHistoryFolderId is not Guid id) return;
            HistoryFolderState next = _historyFolders.Copy();
            next.RenameFolder(id, name);
            await SaveHistoryFoldersAsync(next, cancellationToken).ConfigureAwait(true);
            StatusText = $"Renamed folder to {name.Trim()}.";
        });

    public Task RemoveHistoryFolderAsync(CancellationToken cancellationToken) =>
        RunHistoryFolderOperationAsync(async () =>
        {
            if (_selectedHistoryFolderId is not Guid id) return;
            HistoryFolderState next = _historyFolders.Copy();
            next.RemoveFolder(id);
            await SaveHistoryFoldersAsync(next, cancellationToken).ConfigureAwait(true);
            _selectedHistoryFolderId = null;
            _historyUnfiledOnly = true;
            StatusText = "Folder removed. Its captures are now in Unfiled.";
        });

    public Task MoveCaptureToHistoryFolderAsync(string documentId, Guid? folderId, CancellationToken cancellationToken) =>
        RunHistoryFolderOperationAsync(async () =>
        {
            if (!Guid.TryParse(documentId, out Guid value) || !_allRecentDocuments.Any(document => document.Id.Value == value)) return;
            HistoryFolderState next = _historyFolders.Copy();
            next.MoveDocument(new DocumentId(value), folderId);
            await SaveHistoryFoldersAsync(next, cancellationToken).ConfigureAwait(true);
            StatusText = folderId.HasValue ? "Capture moved to folder." : "Capture moved to Unfiled.";
        });

    public Guid? GetCaptureHistoryFolderId(string documentId) => Guid.TryParse(documentId, out Guid value)
        ? _historyFolders.GetFolderId(new DocumentId(value)) : null;

    public Task<string?> PickHistoryImportFolderAsync(CancellationToken cancellationToken) => _storageLocationPicker is null
        ? Task.FromResult<string?>(null) : _storageLocationPicker.PickStorageRootAsync(StorageRoot, cancellationToken);

    public Task ImportHistoryFolderAsync(string path, string name, bool includeSubfolders, CancellationToken cancellationToken) =>
        ReadHistoryFoldersAsync([new(null, path, name, includeSubfolders, false)], cancellationToken);

    public Task AddMappedHistoryFolderAsync(string path, string name, bool includeSubfolders, CancellationToken cancellationToken) =>
        ReadHistoryFoldersAsync([new(null, path, name, includeSubfolders, true)], cancellationToken);

    public Task RefreshHistoryFoldersAsync(CancellationToken cancellationToken)
    {
        HistoryFolderReadRequest[] requests = _historyFolders.Folders
            .Where(folder => folder.SourcePath is not null && (_selectedHistoryFolderId is null || folder.Id == _selectedHistoryFolderId))
            .Select(folder => new HistoryFolderReadRequest(folder.Id, folder.SourcePath!, folder.Name, folder.IncludeSubfolders, true)).ToArray();
        return requests.Length == 0 ? Task.CompletedTask : ReadHistoryFoldersAsync(requests, cancellationToken);
    }

    public bool TrySelectMappedHistoryFolder(string path)
    {
        string normalized = HistoryFolderState.NormalizeSourcePath(path);
        int index = HistoryFolders.ToList().FindIndex(folder => string.Equals(folder.SourcePath, normalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        SelectHistoryFolder(index);
        StatusText = "This folder is already mapped. Use Refresh to find new images.";
        return true;
    }

    private sealed record HistoryFolderReadRequest(Guid? Id, string Path, string Name, bool IncludeSubfolders, bool Mapped);

    private async Task ReadHistoryFoldersAsync(IReadOnlyList<HistoryFolderReadRequest> requests, CancellationToken cancellationToken)
    {
        if (!CanManageHistoryFolders || _imageImportService is null || _documentRepository is null) return;
        _historyFolderOperationInProgress = true;
        _folderImportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken importToken = _folderImportCancellation.Token;
        SetCaptureInProgress(true);
        _folderImportProgressText = "Finding images…";
        CaptureNoticeIsOpen = false;
        NotifyHistoryFolderStateChanged();
        int importedCount = 0;
        bool stopped = false;
        var failures = new List<string>();
        string? createdFolderName = null;
        try
        {
            foreach (HistoryFolderReadRequest request in requests)
            {
                importToken.ThrowIfCancellationRequested();
                IReadOnlyList<string> paths;
                try
                {
                    paths = await Task.Run(() => DocumentFolderImporter.FindImages(request.Path, request.IncludeSubfolders), importToken).ConfigureAwait(true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{request.Name}: {exception.Message}");
                    continue;
                }
                importToken.ThrowIfCancellationRequested();
                if (paths.Count == 0 && !request.Mapped)
                {
                    failures.Add($"{request.Name}: No PNG, JPEG, or BMP images found in the selected scope.");
                    continue;
                }
                HistoryFolderState next = _historyFolders.Copy();
                HistoryFolder folder;
                if (request.Id is Guid id) folder = next.Folders.Single(folder => folder.Id == id);
                else
                {
                    folder = next.AddFolder(request.Name, request.Mapped ? request.Path : null, request.IncludeSubfolders);
                    await SaveHistoryFoldersAsync(next, importToken).ConfigureAwait(true);
                    _selectedHistoryFolderId = folder.Id;
                    _historyUnfiledOnly = false;
                    createdFolderName = folder.Name;
                    RefreshHistoryFolderItems();
                }
                var progress = new Progress<FolderImportProgress>(value =>
                {
                    void UpdateProgress()
                    {
                        _folderImportProgressText = $"{folder.Name}: {value.Completed} of {value.Total} images · {value.FileName}";
                        OnPropertyChanged(nameof(FolderImportProgressText));
                    }
                    if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess) UpdateProgress();
                    else _dispatcherQueue.TryEnqueue(UpdateProgress);
                });
                var importer = new DocumentFolderImporter(_imageImportService, _documentRepository, _historyFolderStore!);
                FolderImportResult result = await importer.ImportAsync(paths, next.Copy(), folder.Id, progress, importToken).ConfigureAwait(true);
                importedCount += result.ImportedDocuments.Count;
                failures.AddRange(result.Failures.Select(failure => $"{folder.Name} / {failure.FileName}: {failure.Message}"));
                if (result.ErrorMessage is not null) failures.Add($"{folder.Name}: {result.ErrorMessage}");
                // Reload the last committed membership even if a later image failed.
                _historyFolders = await _historyFolderStore!.LoadFoldersAsync(CancellationToken.None).ConfigureAwait(true);
                if (result.WasCancelled) { stopped = true; break; }
            }
        }
        catch (OperationCanceledException) when (importToken.IsCancellationRequested) { stopped = true; }
        catch (Exception exception) { failures.Add(exception.Message); }
        finally
        {
            try
            {
                _historyPagination.Reset();
                await RefreshRecentDocumentsAsync(CancellationToken.None).ConfigureAwait(true);
                if (!HasCurrentDocument) await OpenMostRecentDocumentAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) { failures.Add(exception.Message); }
            _folderImportCancellation.Dispose();
            _folderImportCancellation = null;
            _historyFolderOperationInProgress = false;
            SetCaptureInProgress(false);
            RefreshHistoryFolderItems();
        }
        string summary = requests.Any(request => !request.Mapped)
            ? $"Imported {importedCount} image{(importedCount == 1 ? "" : "s")}{(createdFolderName is null ? "" : $" into {createdFolderName}")}."
            : createdFolderName is not null ? $"Added {createdFolderName}. {importedCount} images copied."
            : $"Refresh complete. {importedCount} new image{(importedCount == 1 ? "" : "s")} added.";
        if (stopped) summary = $"Stopped. {summary}";
        if (failures.Count > 0) summary += $" {failures.Count} could not be imported or refreshed.";
        StatusText = summary;
        if (failures.Count > 0)
            ShowCaptureNotice("Capture folders", $"{summary}\n{string.Join("\n", failures.Take(3))}", InfoBarSeverity.Warning);
    }

    public void CancelHistoryFolderImport() => _folderImportCancellation?.Cancel();

    public void ShowHistoryFolderError(string message) => ShowCaptureNotice("Capture folders", message, InfoBarSeverity.Error);

    private async Task RunHistoryFolderOperationAsync(Func<Task> operation)
    {
        if (!CanManageHistoryFolders) return;
        _historyFolderOperationInProgress = true;
        NotifyHistoryFolderStateChanged();
        try
        {
            await operation().ConfigureAwait(true);
            _historyPagination.Reset();
            RefreshHistoryFolderItems();
            ApplyRecentDocumentFilter(refreshThumbnails: true);
        }
        catch (Exception exception) { ShowCaptureNotice("Folder change failed", exception.Message, InfoBarSeverity.Error); }
        finally { _historyFolderOperationInProgress = false; NotifyHistoryFolderStateChanged(); }
    }

    private async Task SaveHistoryFoldersAsync(HistoryFolderState state, CancellationToken cancellationToken)
    {
        await _historyFolderStore!.SaveFoldersAsync(state, cancellationToken).ConfigureAwait(true);
        _historyFolders = state;
    }

    private bool MatchesHistoryFolder(DocumentSummary document)
    {
        Guid? folderId = _historyFolders.GetFolderId(document.Id);
        return _selectedHistoryFolderId is Guid id ? folderId == id : !_historyUnfiledOnly || folderId is null;
    }

    private void RefreshHistoryFolderItems()
    {
        _refreshingHistoryFolders = true;
        try
        {
            var counts = _allRecentDocuments.GroupBy(document => _historyFolders.GetFolderId(document.Id) ?? Guid.Empty)
                .ToDictionary(group => group.Key, group => group.Count());
            HistoryFolders.Clear();
            HistoryFolders.Add(new(null, "All captures", _allRecentDocuments.Count));
            HistoryFolders.Add(new(null, "Unfiled", counts.GetValueOrDefault(Guid.Empty), true));
            foreach (HistoryFolder folder in _historyFolders.Folders.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
                HistoryFolders.Add(new(folder.Id, folder.Name, counts.GetValueOrDefault(folder.Id), SourcePath: folder.SourcePath));
            NotifyHistoryFolderStateChanged();
        }
        finally { _refreshingHistoryFolders = false; }
    }

    private void NotifyHistoryFolderStateChanged()
    {
        OnPropertyChanged(nameof(HistoryFolderIndex));
        OnPropertyChanged(nameof(CanRefreshHistoryFolders));
        OnPropertyChanged(nameof(SelectedHistoryFolderLocation));
        OnPropertyChanged(nameof(SelectedHistoryFolderLocationVisibility));
        OnPropertyChanged(nameof(SelectedHistoryFolder));
        OnPropertyChanged(nameof(CanManageHistoryFolders));
        OnPropertyChanged(nameof(CanEditSelectedHistoryFolder));
        OnPropertyChanged(nameof(CanMoveCurrentCaptureToHistoryFolder));
        OnPropertyChanged(nameof(FolderImportProgressVisibility));
        OnPropertyChanged(nameof(FolderImportProgressText));
    }
}
