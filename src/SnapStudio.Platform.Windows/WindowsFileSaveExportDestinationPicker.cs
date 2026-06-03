using SnapStudio.Core.Export;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsFileSaveExportDestinationPicker : IExportDestinationPicker
{
    private readonly nint _ownerWindowHandle;

    public WindowsFileSaveExportDestinationPicker(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
    }

    public async Task<ExportDestination?> PickDestinationAsync(
        ExportFormat format,
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
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
        };

        ConfigureFormat(picker, format);
        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        global::Windows.Storage.StorageFile? file = await picker
            .PickSaveFileAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        return file is null
            ? null
            : new ExportDestination(format, file.Path);
    }

    private static void ConfigureFormat(FileSavePicker picker, ExportFormat format)
    {
        switch (format)
        {
            case ExportFormat.Png:
                picker.FileTypeChoices.Add("PNG image", [".png"]);
                picker.DefaultFileExtension = ".png";
                break;
            case ExportFormat.Jpeg:
                picker.FileTypeChoices.Add("JPEG image", [".jpg", ".jpeg"]);
                picker.DefaultFileExtension = ".jpg";
                break;
            case ExportFormat.Pdf:
                picker.FileTypeChoices.Add("PDF document", [".pdf"]);
                picker.DefaultFileExtension = ".pdf";
                break;
            default:
                picker.FileTypeChoices.Add("File", [".bin"]);
                picker.DefaultFileExtension = ".bin";
                break;
        }
    }
}
