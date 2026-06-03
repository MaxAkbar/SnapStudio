using System.Diagnostics;
using SnapStudio.Core.System;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsWorkspaceShellService : IWorkspaceShellService
{
    public Task<WorkspaceShellResult> OpenContainingFolderAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(WorkspaceShellResult.Failed("No path is available for this document."));
        }

        bool isFile = File.Exists(path);
        string? folderPath = isFile
            ? Path.GetDirectoryName(path)
            : Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return Task.FromResult(WorkspaceShellResult.Failed("The containing folder could not be found."));
        }

        try
        {
            ProcessStartInfo startInfo = isFile
                ? new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                }
                : new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                };

            Process.Start(startInfo);

            return Task.FromResult(WorkspaceShellResult.Success());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(WorkspaceShellResult.Failed(
                $"The containing folder could not be opened: {exception.Message}"));
        }
    }

    public Task<WorkspaceShellResult> OpenPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(WorkspaceShellResult.Failed("No path is available to open."));
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return Task.FromResult(WorkspaceShellResult.Failed("The requested path could not be found."));
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            return Task.FromResult(WorkspaceShellResult.Success());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(WorkspaceShellResult.Failed(
                $"The requested path could not be opened: {exception.Message}"));
        }
    }
}
