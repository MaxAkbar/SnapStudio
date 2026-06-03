namespace SnapStudio.Core.System;

public sealed record WorkspaceShellResult(bool Succeeded, string? ErrorMessage)
{
    public static WorkspaceShellResult Success() => new(true, null);

    public static WorkspaceShellResult Failed(string errorMessage) => new(false, errorMessage);
}

public interface IWorkspaceShellService
{
    Task<WorkspaceShellResult> OpenContainingFolderAsync(
        string path,
        CancellationToken cancellationToken);

    Task<WorkspaceShellResult> OpenPathAsync(
        string path,
        CancellationToken cancellationToken);
}
