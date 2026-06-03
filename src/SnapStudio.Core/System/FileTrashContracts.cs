namespace SnapStudio.Core.System;

public sealed record FileTrashResult(bool Succeeded, string? ErrorMessage)
{
    public static FileTrashResult Success() => new(true, null);

    public static FileTrashResult Failed(string errorMessage) => new(false, errorMessage);
}

public interface IFileTrashService
{
    Task<FileTrashResult> MoveFileToTrashAsync(
        string path,
        CancellationToken cancellationToken);
}
