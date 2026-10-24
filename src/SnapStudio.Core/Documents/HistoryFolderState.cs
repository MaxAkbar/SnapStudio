using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public interface IHistoryFolderStore
{
    Task<HistoryFolderState> LoadFoldersAsync(CancellationToken cancellationToken);
    Task SaveFoldersAsync(HistoryFolderState state, CancellationToken cancellationToken);
}

public sealed record HistoryFolder(Guid Id, string Name, string? SourcePath = null, bool IncludeSubfolders = false);
public sealed record LinkedHistoryImage(Guid FolderId, string SourcePath, Guid DocumentId);

// Folder membership is workspace metadata, independent of document edits and undo history.
public sealed record HistoryFolderState
{
    public List<HistoryFolder> Folders { get; init; } = [];
    public Dictionary<Guid, Guid> Assignments { get; init; } = [];
    public List<LinkedHistoryImage> LinkedImages { get; init; } = [];

    public HistoryFolderState Copy() => new() { Folders = [.. Folders], Assignments = new(Assignments), LinkedImages = [.. LinkedImages] };

    public HistoryFolder AddFolder(string name, string? sourcePath = null, bool includeSubfolders = false)
    {
        string normalized = ValidateName(name);
        string? source = sourcePath is null ? null : NormalizeSourcePath(sourcePath);
        if (source is not null && Folders.Any(folder => string.Equals(folder.SourcePath, source, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This folder is already mapped. Select it and use Refresh to find new images.");
        var folder = new HistoryFolder(Guid.NewGuid(), normalized, source, includeSubfolders);
        Folders.Add(folder);
        return folder;
    }

    public void RenameFolder(Guid id, string name)
    {
        int index = Folders.FindIndex(folder => folder.Id == id);
        if (index < 0) throw new ArgumentException("This folder no longer exists.", nameof(id));
        Folders[index] = Folders[index] with { Name = ValidateName(name, id) };
    }

    public void RemoveFolder(Guid id)
    {
        Folders.RemoveAll(folder => folder.Id == id);
        LinkedImages.RemoveAll(image => image.FolderId == id);
        foreach (Guid documentId in Assignments.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray())
            Assignments.Remove(documentId);
    }

    public bool IsLinkedImageKnown(Guid folderId, string path)
    {
        string normalized = NormalizeSourcePath(path);
        return LinkedImages.Any(image => image.FolderId == folderId && string.Equals(image.SourcePath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void RememberLinkedImage(Guid folderId, string path, DocumentId documentId)
    {
        if (!Folders.Any(folder => folder.Id == folderId && folder.SourcePath is not null))
            throw new ArgumentException("This mapped folder no longer exists.", nameof(folderId));
        if (!IsLinkedImageKnown(folderId, path)) LinkedImages.Add(new(folderId, NormalizeSourcePath(path), documentId.Value));
    }

    public static string NormalizeSourcePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public Guid? GetFolderId(DocumentId documentId) =>
        Assignments.TryGetValue(documentId.Value, out Guid id) && Folders.Any(folder => folder.Id == id) ? id : null;

    public void MoveDocument(DocumentId documentId, Guid? folderId)
    {
        if (folderId is Guid id)
        {
            if (!Folders.Any(folder => folder.Id == id)) throw new ArgumentException("This folder no longer exists.", nameof(folderId));
            Assignments[documentId.Value] = id;
        }
        else Assignments.Remove(documentId.Value);
    }

    public string ValidateName(string? name, Guid? exceptId = null)
    {
        string normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 80) throw new ArgumentException("Use a folder name between 1 and 80 characters.");
        if (normalized.Equals("All captures", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Unfiled", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a name other than All captures or Unfiled.");
        if (Folders.Any(folder => folder.Id != exceptId && folder.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A folder with this name already exists.");
        return normalized;
    }
}
