using SnapStudio.Core.Documents;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsToolDesignFilePicker(nint ownerWindowHandle) : IToolDesignFilePicker
{
    public async Task<string?> PickImportAsync(CancellationToken cancellationToken)
    {
        if (ownerWindowHandle == 0) return null;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, ownerWindowHandle);
        return (await picker.PickSingleFileAsync().AsTask(cancellationToken))?.Path;
    }

    public async Task<string?> PickExportAsync(CancellationToken cancellationToken)
    {
        if (ownerWindowHandle == 0) return null;
        var picker = new FileSavePicker { SuggestedFileName = "SnapStudio tool designs", DefaultFileExtension = ".json" };
        picker.FileTypeChoices.Add("SnapStudio tool designs", [".json"]);
        InitializeWithWindow.Initialize(picker, ownerWindowHandle);
        return (await picker.PickSaveFileAsync().AsTask(cancellationToken))?.Path;
    }
}
