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

        return await ImportFileAsync(file.Path, cancellationToken).ConfigureAwait(true);
    }

    public Task<CaptureOutcome> ImportFileAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return CaptureOutcome.Success(CreateCaptureFromFile(path));
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
    }, cancellationToken);

    private CaptureResult CreateCaptureFromFile(string sourcePath)
    {
        Directory.CreateDirectory(_sourceImageDirectory);

        string extension = Path.GetExtension(sourcePath);
        string copiedPath = Path.Combine(
            _sourceImageDirectory,
            $"import-{Guid.NewGuid():N}{extension}");
        // Validate before copying so a broken image does not leave an unused source file.
        using var bitmap = new Bitmap(sourcePath);
        int width = bitmap.Width;
        int height = bitmap.Height;
        File.Copy(sourcePath, copiedPath, overwrite: false);

        DateTimeOffset importedAtUtc = DateTimeOffset.UtcNow;
        return new CaptureResult(
            CaptureId.New(),
            importedAtUtc,
            new ImageAsset(copiedPath, width, height, ImagePixelFormat.Bgra32),
            new Dictionary<string, string>
            {
                ["title"] = Path.GetFileNameWithoutExtension(sourcePath),
                ["source"] = "import",
                ["originalPath"] = sourcePath,
                ["importedAtUtc"] = importedAtUtc.ToString("O")
            });
    }
}
