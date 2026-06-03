using Microsoft.VisualBasic.FileIO;
using SnapStudio.Core.System;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsFileTrashService : IFileTrashService
{
    public Task<FileTrashResult> MoveFileToTrashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(FileTrashResult.Failed("No source image is available for this capture."));
        }

        if (!File.Exists(path))
        {
            return Task.FromResult(FileTrashResult.Failed("The source image file could not be found."));
        }

        try
        {
            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);

            return Task.FromResult(FileTrashResult.Success());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(FileTrashResult.Failed(
                $"The source image could not be moved to the recycle bin: {exception.Message}"));
        }
    }
}
