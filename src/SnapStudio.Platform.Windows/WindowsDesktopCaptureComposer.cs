using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsDesktopCaptureImage(ImageAsset Image, RectD Bounds);

/// <summary>Places monitor images at their physical desktop coordinates, including negative origins.</summary>
public static class WindowsDesktopCaptureComposer
{
    public static ImageAsset Save(
        IReadOnlyList<WindowsDesktopCaptureImage> images,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (images.Count == 0 || images.Any(image => !ValidBounds(image.Bounds)))
            throw new ArgumentException("At least one valid display image is required.", nameof(images));

        double left = images.Min(image => image.Bounds.X);
        double top = images.Min(image => image.Bounds.Y);
        int width = checked((int)(images.Max(image => image.Bounds.Right) - left));
        int height = checked((int)(images.Max(image => image.Bounds.Bottom) - top));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            foreach (WindowsDesktopCaptureImage image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = new Bitmap(image.Image.Path);
                if (source.Width != image.Bounds.Width || source.Height != image.Bounds.Height)
                    throw new InvalidOperationException("A display's resolution changed. Start the capture again.");
                // Explicit pixel rectangles avoid GDI+'s implicit scaling from the PNG's DPI.
                var destination = new Rectangle(checked((int)(image.Bounds.X - left)), checked((int)(image.Bounds.Y - top)), source.Width, source.Height);
                graphics.DrawImage(source, destination, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        bitmap.Save(outputPath, ImageFormat.Png);
        return new ImageAsset(outputPath, width, height, ImagePixelFormat.Bgra32);
    }

    private static bool ValidBounds(RectD bounds) => double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
        && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0
        && bounds.X == Math.Truncate(bounds.X) && bounds.Y == Math.Truncate(bounds.Y)
        && bounds.Width == Math.Truncate(bounds.Width) && bounds.Height == Math.Truncate(bounds.Height);
}
