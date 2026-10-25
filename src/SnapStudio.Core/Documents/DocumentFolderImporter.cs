using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public sealed record FolderImportProgress(int Completed, int Total, string FileName);
public sealed record FolderImportFailure(string FileName, string Message);
public sealed record FolderImportResult(
    IReadOnlyList<DocumentId> ImportedDocuments,
    IReadOnlyList<FolderImportFailure> Failures,
    bool WasCancelled,
    string? ErrorMessage = null);

public sealed class DocumentFolderImporter(
    IImageImportService images,
    IDocumentRepository documents,
    IHistoryFolderStore folderStore)
{
    public static IReadOnlyList<string> FindImages(string path, bool includeSubfolders)
    {
        var extensions = new HashSet<string>([".png", ".jpg", ".jpeg", ".bmp"], StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        })
            .Where(file => extensions.Contains(Path.GetExtension(file)))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<FolderImportResult> ImportAsync(
        IReadOnlyList<string> paths,
        HistoryFolderState folders,
        Guid folderId,
        IProgress<FolderImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        HistoryFolder folder = folders.Folders.SingleOrDefault(folder => folder.Id == folderId)
            ?? throw new ArgumentException("This folder no longer exists.", nameof(folderId));
        bool mapped = folder.SourcePath is not null;
        if (mapped)
            paths = paths.Select(HistoryFolderState.NormalizeSourcePath).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !folders.IsLinkedImageKnown(folderId, path)).ToArray();
        var imported = new List<DocumentId>();
        var failures = new List<FolderImportFailure>();
        try
        {
            for (int index = 0; index < paths.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = paths[index];
                progress?.Report(new(index, paths.Count, Path.GetFileName(path)));
                CaptureOutcome outcome = await images.ImportFileAsync(path, cancellationToken).ConfigureAwait(false);
                if (!outcome.Succeeded || outcome.Capture is not CaptureResult capture)
                {
                    failures.Add(new(Path.GetFileName(path), outcome.Failure?.Message ?? "This image could not be opened."));
                    continue;
                }

                CaptureDocument document = await documents.CreateFromCaptureAsync(capture, cancellationToken).ConfigureAwait(false);
                imported.Add(document.Id);
                folders.MoveDocument(document.Id, folderId);
                if (mapped) folders.RememberLinkedImage(folderId, path, document.Id);
                // Persist each completed image's membership; cancelling retains the completed part of the import.
                await folderStore.SaveFoldersAsync(folders, CancellationToken.None).ConfigureAwait(false);
            }
            progress?.Report(new(paths.Count, paths.Count, string.Empty));
            return new(imported, failures, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(imported, failures, true);
        }
        catch (Exception exception)
        {
            return new(imported, failures, false, exception.Message);
        }
    }
}
