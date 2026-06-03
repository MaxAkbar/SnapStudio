using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed class SystemDrawingScrollingFrameImageWriter : IWindowsScrollingFrameImageWriter
{
    public ImageAsset Capture(
        RectD bounds,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        Rectangle rectangle = ToRectangle(bounds);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var bitmap = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            rectangle.Left,
            rectangle.Top,
            0,
            0,
            new Size(rectangle.Width, rectangle.Height),
            CopyPixelOperation.SourceCopy);

        cancellationToken.ThrowIfCancellationRequested();
        bitmap.Save(outputPath, ImageFormat.Png);

        return new ImageAsset(outputPath, rectangle.Width, rectangle.Height, ImagePixelFormat.Bgra32);
    }

    private static Rectangle ToRectangle(RectD bounds)
    {
        int left = (int)Math.Floor(Math.Min(bounds.X, bounds.Right));
        int top = (int)Math.Floor(Math.Min(bounds.Y, bounds.Bottom));
        int right = (int)Math.Ceiling(Math.Max(bounds.X, bounds.Right));
        int bottom = (int)Math.Ceiling(Math.Max(bounds.Y, bounds.Bottom));

        return new Rectangle(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }
}
