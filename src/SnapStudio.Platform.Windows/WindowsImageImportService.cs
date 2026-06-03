using System.Drawing;
using System.Runtime.InteropServices;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsImageImportService : IImageImportService
{
    private readonly nint _ownerWindowHandle;
    private readonly string _sourceImageDirectory;

    public WindowsImageImportService(
        nint ownerWindowHandle,
        string sourceImageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImageDirectory);

        _ownerWindowHandle = ownerWindowHandle;
        _sourceImageDirectory = Path.GetFullPath(sourceImageDirectory);
    }

    public async Task<CaptureOutcome> ImportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_ownerWindowHandle == 0)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.Unsupported,
                "Image import requires a visible SnapStudio window."));
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");

        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        global::Windows.Storage.StorageFile? file = await picker
            .PickSingleFileAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        if (file is null)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.Cancelled,
                "Open cancelled."));
        }

        try
        {
            return CaptureOutcome.Success(CreateCaptureFromFile(file.Path));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or ExternalException
            or IOException
            or UnauthorizedAccessException)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.Unknown,
                $"The selected image could not be opened: {exception.Message}",
                exception));
        }
    }

    private CaptureResult CreateCaptureFromFile(string sourcePath)
    {
        Directory.CreateDirectory(_sourceImageDirectory);

        string extension = Path.GetExtension(sourcePath);
        string copiedPath = Path.Combine(
            _sourceImageDirectory,
            $"import-{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, copiedPath, overwrite: false);
        using var bitmap = new Bitmap(copiedPath);

        DateTimeOffset importedAtUtc = DateTimeOffset.UtcNow;
        return new CaptureResult(
            CaptureId.New(),
            importedAtUtc,
            new ImageAsset(copiedPath, bitmap.Width, bitmap.Height, ImagePixelFormat.Bgra32),
            new Dictionary<string, string>
            {
                ["title"] = Path.GetFileNameWithoutExtension(sourcePath),
                ["source"] = "import",
                ["originalPath"] = sourcePath,
                ["importedAtUtc"] = importedAtUtc.ToString("O")
            });
    }
}
