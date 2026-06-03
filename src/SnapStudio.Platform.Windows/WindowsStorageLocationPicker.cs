using SnapStudio.Core.System;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsStorageLocationPicker : IStorageLocationPicker
{
    private readonly nint _ownerWindowHandle;

    public WindowsStorageLocationPicker(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
    }

    public async Task<string?> PickStorageRootAsync(
        string currentStorageRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0)
        {
            return null;
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        global::Windows.Storage.StorageFolder? folder = await picker
            .PickSingleFolderAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        return folder?.Path;
    }
}
