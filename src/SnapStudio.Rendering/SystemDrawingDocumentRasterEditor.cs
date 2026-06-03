using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Rendering;

public sealed class SystemDrawingDocumentRasterEditor : IDocumentRasterEditor
{
    public Task<RasterEditResult> CropAsync(
        RasterCropRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(request.SourceImage.Path))
                {
                    return RasterEditResult.Failed("The source image could not be found.");
                }

                using var source = new Bitmap(request.SourceImage.Path);
                Rectangle cropBounds = ClampRectangle(
                    request.SourceBounds,
                    source.Width,
                    source.Height);
                using var output = new Bitmap(
                    cropBounds.Width,
                    cropBounds.Height,
                    PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(output))
                {
                    ConfigureGraphics(graphics);
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, cropBounds.Width, cropBounds.Height),
                        cropBounds,
                        GraphicsUnit.Pixel);
                }

                string outputPath = CreateOutputPath(request.OutputDirectory, "crop");
                output.Save(outputPath, ImageFormat.Png);

                return RasterEditResult.Success(new ImageAsset(
                    outputPath,
                    output.Width,
                    output.Height,
                    ImagePixelFormat.Bgra32));
            },
            cancellationToken);
    }

    public Task<RasterEditResult> ResizeAsync(
        RasterResizeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(request.SourceImage.Path))
                {
                    return RasterEditResult.Failed("The source image could not be found.");
                }

                int width = Math.Max(1, request.Width);
                int height = Math.Max(1, request.Height);
                using var source = new Bitmap(request.SourceImage.Path);
                using var output = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(output))
                {
                    ConfigureGraphics(graphics);
                    graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                }

                string outputPath = CreateOutputPath(request.OutputDirectory, "resize");
                output.Save(outputPath, ImageFormat.Png);

                return RasterEditResult.Success(new ImageAsset(
                    outputPath,
                    output.Width,
                    output.Height,
                    ImagePixelFormat.Bgra32));
            },
            cancellationToken);
    }

    private static string CreateOutputPath(string outputDirectory, string operationName)
    {
        Directory.CreateDirectory(outputDirectory);
        return Path.Combine(outputDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{operationName}-{Guid.NewGuid():N}.png");
    }

    private static Rectangle ClampRectangle(
        RectD bounds,
        int maximumWidth,
        int maximumHeight)
    {
        RectD normalized = Normalize(bounds);
        int left = Math.Clamp((int)Math.Floor(normalized.X), 0, maximumWidth);
        int top = Math.Clamp((int)Math.Floor(normalized.Y), 0, maximumHeight);
        int right = Math.Clamp((int)Math.Ceiling(normalized.Right), left, maximumWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(normalized.Bottom), top, maximumHeight);

        return new Rectangle(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }
}
