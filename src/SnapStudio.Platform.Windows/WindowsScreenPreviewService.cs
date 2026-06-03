using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsScreenPreviewService : IScreenPreviewService
{
    private readonly string _outputDirectory;

    public WindowsScreenPreviewService(string? outputDirectory = null)
    {
        _outputDirectory = Path.GetFullPath(outputDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "SnapStudio",
            "ScreenPreviews"));
    }

    public Task<ScreenPreviewImage?> CapturePreviewAsync(
        RectD virtualScreenBounds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (virtualScreenBounds.Width <= 0 || virtualScreenBounds.Height <= 0)
        {
            return Task.FromResult<ScreenPreviewImage?>(null);
        }

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_outputDirectory);

                int width = Math.Max(1, (int)Math.Round(virtualScreenBounds.Width));
                int height = Math.Max(1, (int)Math.Round(virtualScreenBounds.Height));
                string outputPath = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.png");

                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(
                    (int)Math.Round(virtualScreenBounds.X),
                    (int)Math.Round(virtualScreenBounds.Y),
                    0,
                    0,
                    new Size(width, height),
                    CopyPixelOperation.SourceCopy);

                bitmap.Save(outputPath, ImageFormat.Png);
                return (ScreenPreviewImage?)new ScreenPreviewImage(outputPath, width, height);
            },
            cancellationToken);
    }
}
