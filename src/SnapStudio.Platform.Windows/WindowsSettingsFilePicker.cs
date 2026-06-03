using SnapStudio.Core.System;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsSettingsFilePicker : ISettingsFilePicker
{
    private readonly nint _ownerWindowHandle;

    public WindowsSettingsFilePicker(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
    }

    public async Task<string?> PickSettingsExportPathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0)
        {
            return null;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            DefaultFileExtension = ".json"
        };
        picker.FileTypeChoices.Add("SnapStudio settings", [".json"]);
        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        global::Windows.Storage.StorageFile? file = await picker
            .PickSaveFileAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        return file?.Path;
    }

    public async Task<string?> PickSettingsImportPathAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        global::Windows.Storage.StorageFile? file = await picker
            .PickSingleFileAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        return file?.Path;
    }
}
