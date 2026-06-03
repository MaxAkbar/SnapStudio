using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using SnapStudio.Core.Capture;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class SystemDrawingVerticalOverlapScrollingStitcher : IScrollingStitcher
{
    private const double DefaultMinimumOverlapRatio = 0.15;
    private const double MaximumConfidentAverageDifference = 12;
    private const double StickyRegionAverageDifference = 8;
    private const double MaximumStickyRegionRatio = 0.25;
    private const int SampleColumnCount = 24;
    private const int SampleRowCount = 18;
    private const int MaximumStickyRegionRows = 160;
    private const int MinimumStickyRegionRows = 6;

    public Task<ScrollingStitchResult> StitchAsync(
        ScrollingStitchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ScrollingCaptureFailure? validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
        {
            return Task.FromResult(ScrollingStitchResult.Failed(validationFailure));
        }

        return Task.Run(
            () => StitchCore(request, cancellationToken),
            cancellationToken);
    }

    private static ScrollingStitchResult StitchCore(
        ScrollingStitchRequest request,
        CancellationToken cancellationToken)
    {
        var bitmaps = new List<Bitmap>();
        try
        {
            foreach (ScrollingCaptureFrame frame in request.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bitmaps.Add(LoadBitmap(frame.Image.Path));
            }

            double minimumOverlapRatio = ResolveMinimumOverlapRatio(request.Options);
            IReadOnlyList<FrameVisibleRegion> frameRegions = DetectFrameRegions(bitmaps, cancellationToken);
            IReadOnlyList<StitchSegment> segments = BuildSegments(
                bitmaps,
                frameRegions,
                minimumOverlapRatio,
                cancellationToken);
            int outputWidth = bitmaps.Max(bitmap => bitmap.Width);
            FrameVisibleRegion firstRegion = frameRegions[0];
            int outputHeight = Math.Max(1, firstRegion.Height + segments.Sum(segment => segment.Height));
            string outputDirectory = Path.GetFullPath(request.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, $"scroll-stitch-{Guid.NewGuid():N}.png");

            using var output = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(output))
            {
                ConfigureGraphics(graphics);
                graphics.Clear(Color.Transparent);
                DrawBitmap(graphics, bitmaps[0], firstRegion.Top, firstRegion.Height, destinationTop: 0);

                int destinationTop = firstRegion.Height;
                foreach (StitchSegment segment in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (segment.Height <= 0)
                    {
                        continue;
                    }

                    DrawBitmap(
                        graphics,
                        bitmaps[segment.FrameIndex],
                        segment.SourceTop,
                        segment.Height,
                        destinationTop);
                    destinationTop += segment.Height;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            output.Save(outputPath, ImageFormat.Png);

            ImageAsset image = new(outputPath, output.Width, output.Height, ImagePixelFormat.Bgra32);
            return ScrollingStitchResult.Success(image, CreateDiagnostics(
                request.Frames.Count,
                output.Width,
                output.Height,
                minimumOverlapRatio,
                segments,
                frameRegions));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or ExternalException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return ScrollingStitchResult.Failed(new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.StitchFailed,
                $"Scrolling capture frames could not be stitched: {exception.Message}",
                exception));
        }
        finally
        {
            foreach (Bitmap bitmap in bitmaps)
            {
                bitmap.Dispose();
            }
        }
    }

    private static IReadOnlyList<StitchSegment> BuildSegments(
        IReadOnlyList<Bitmap> bitmaps,
        IReadOnlyList<FrameVisibleRegion> frameRegions,
        double minimumOverlapRatio,
        CancellationToken cancellationToken)
    {
        var segments = new List<StitchSegment>();
        for (int index = 1; index < bitmaps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OverlapMatch match = FindBestVerticalOverlap(
                bitmaps[index - 1],
                frameRegions[index - 1],
                bitmaps[index],
                frameRegions[index],
                minimumOverlapRatio,
                cancellationToken);
            int overlap = match.IsConfident ? match.Rows : 0;
            int sourceHeight = Math.Max(0, frameRegions[index].Height - overlap);
            segments.Add(new StitchSegment(
                index,
                frameRegions[index].Top + overlap,
                sourceHeight,
                overlap,
                match.Score,
                match.IsConfident));
        }

        return segments;
    }

    private static OverlapMatch FindBestVerticalOverlap(
        Bitmap previous,
        FrameVisibleRegion previousRegion,
        Bitmap current,
        FrameVisibleRegion currentRegion,
        double minimumOverlapRatio,
        CancellationToken cancellationToken)
    {
        int maximumOverlap = Math.Min(previousRegion.Height, currentRegion.Height);
        if (maximumOverlap <= 0)
        {
            return new OverlapMatch(0, double.MaxValue, IsConfident: false);
        }

        int minimumOverlap = Math.Clamp(
            (int)Math.Round(maximumOverlap * minimumOverlapRatio),
            1,
            maximumOverlap);
        var best = new OverlapMatch(0, double.MaxValue, IsConfident: false);

        for (int overlap = minimumOverlap; overlap <= maximumOverlap; overlap++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double score = ScoreOverlap(previous, previousRegion, current, currentRegion, overlap);
            if (score < best.Score
                || (Math.Abs(score - best.Score) < double.Epsilon && overlap > best.Rows))
            {
                best = new OverlapMatch(
                    overlap,
                    score,
                    score <= MaximumConfidentAverageDifference);
            }
        }

        return best.IsConfident ? best : best with { Rows = 0 };
    }

    private static double ScoreOverlap(
        Bitmap previous,
        FrameVisibleRegion previousRegion,
        Bitmap current,
        FrameVisibleRegion currentRegion,
        int overlap)
    {
        int width = Math.Min(previous.Width, current.Width);
        int columnStep = Math.Max(1, width / SampleColumnCount);
        int rowStep = Math.Max(1, overlap / SampleRowCount);
        long totalDifference = 0;
        int sampleCount = 0;

        for (int y = 0; y < overlap; y += rowStep)
        {
            int previousY = previousRegion.Top + previousRegion.Height - overlap + y;
            int currentY = currentRegion.Top + y;
            for (int x = 0; x < width; x += columnStep)
            {
                Color previousPixel = previous.GetPixel(x, previousY);
                Color currentPixel = current.GetPixel(x, currentY);
                totalDifference += Math.Abs(previousPixel.R - currentPixel.R)
                    + Math.Abs(previousPixel.G - currentPixel.G)
                    + Math.Abs(previousPixel.B - currentPixel.B);
                sampleCount++;
            }
        }

        return sampleCount == 0
            ? double.MaxValue
            : totalDifference / (sampleCount * 3.0);
    }

    private static IReadOnlyList<FrameVisibleRegion> DetectFrameRegions(
        IReadOnlyList<Bitmap> bitmaps,
        CancellationToken cancellationToken)
    {
        var regions = new List<FrameVisibleRegion>();
        for (int index = 0; index < bitmaps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int topCrop = index == 0
                ? 0
                : DetectMatchingTopBand(bitmaps[index - 1], bitmaps[index], cancellationToken);
            int bottomCrop = index == bitmaps.Count - 1
                ? 0
                : DetectMatchingBottomBand(bitmaps[index], bitmaps[index + 1], cancellationToken);

            regions.Add(FrameVisibleRegion.Create(index, bitmaps[index].Height, topCrop, bottomCrop));
        }

        return regions;
    }

    private static int DetectMatchingTopBand(
        Bitmap previous,
        Bitmap current,
        CancellationToken cancellationToken)
    {
        int maximumRows = ResolveMaximumStickyRows(previous, current);
        int matchingRows = 0;
        for (int y = 0; y < maximumRows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ScoreRow(previous, y, current, y) > StickyRegionAverageDifference)
            {
                break;
            }

            matchingRows++;
        }

        return matchingRows >= MinimumStickyRegionRows ? matchingRows : 0;
    }

    private static int DetectMatchingBottomBand(
        Bitmap current,
        Bitmap next,
        CancellationToken cancellationToken)
    {
        int maximumRows = ResolveMaximumStickyRows(current, next);
        int matchingRows = 0;
        for (int offset = 0; offset < maximumRows; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int currentY = current.Height - 1 - offset;
            int nextY = next.Height - 1 - offset;
            if (ScoreRow(current, currentY, next, nextY) > StickyRegionAverageDifference)
            {
                break;
            }

            matchingRows++;
        }

        return matchingRows >= MinimumStickyRegionRows ? matchingRows : 0;
    }

    private static int ResolveMaximumStickyRows(Bitmap first, Bitmap second)
    {
        int height = Math.Min(first.Height, second.Height);
        return Math.Clamp(
            (int)Math.Round(height * MaximumStickyRegionRatio),
            0,
            Math.Min(MaximumStickyRegionRows, height));
    }

    private static double ScoreRow(
        Bitmap first,
        int firstY,
        Bitmap second,
        int secondY)
    {
        int width = Math.Min(first.Width, second.Width);
        int columnStep = Math.Max(1, width / SampleColumnCount);
        long totalDifference = 0;
        int sampleCount = 0;

        for (int x = 0; x < width; x += columnStep)
        {
            Color firstPixel = first.GetPixel(x, firstY);
            Color secondPixel = second.GetPixel(x, secondY);
            totalDifference += Math.Abs(firstPixel.R - secondPixel.R)
                + Math.Abs(firstPixel.G - secondPixel.G)
                + Math.Abs(firstPixel.B - secondPixel.B);
            sampleCount++;
        }

        return sampleCount == 0
            ? double.MaxValue
            : totalDifference / (sampleCount * 3.0);
    }

    private static Bitmap LoadBitmap(string path)
    {
        using var loaded = new Bitmap(path);
        return new Bitmap(loaded);
    }

    private static void DrawBitmap(
        Graphics graphics,
        Bitmap bitmap,
        int sourceTop,
        int height,
        int destinationTop)
    {
        var source = new Rectangle(0, sourceTop, bitmap.Width, height);
        var destination = new Rectangle(0, destinationTop, bitmap.Width, height);
        graphics.DrawImage(bitmap, destination, source, GraphicsUnit.Pixel);
    }

    private static IReadOnlyDictionary<string, string> CreateDiagnostics(
        int frameCount,
        int outputWidth,
        int outputHeight,
        double minimumOverlapRatio,
        IReadOnlyList<StitchSegment> segments,
        IReadOnlyList<FrameVisibleRegion> frameRegions)
    {
        var diagnostics = new Dictionary<string, string>
        {
            ["stitchMethod"] = "vertical-overlap",
            ["frameCount"] = frameCount.ToString(CultureInfo.InvariantCulture),
            ["outputWidth"] = outputWidth.ToString(CultureInfo.InvariantCulture),
            ["outputHeight"] = outputHeight.ToString(CultureInfo.InvariantCulture),
            ["minimumOverlapRatio"] = minimumOverlapRatio.ToString(CultureInfo.InvariantCulture)
        };

        foreach (StitchSegment segment in segments)
        {
            string prefix = $"overlap.{segment.FrameIndex.ToString(CultureInfo.InvariantCulture)}";
            diagnostics[$"{prefix}.rows"] = segment.Overlap.ToString(CultureInfo.InvariantCulture);
            diagnostics[$"{prefix}.score"] = segment.Score.ToString("F3", CultureInfo.InvariantCulture);
            diagnostics[$"{prefix}.confident"] = segment.IsConfident.ToString(CultureInfo.InvariantCulture);
        }

        foreach (FrameVisibleRegion region in frameRegions)
        {
            string prefix = $"sticky.{region.FrameIndex.ToString(CultureInfo.InvariantCulture)}";
            diagnostics[$"{prefix}.topRows"] = region.TopCrop.ToString(CultureInfo.InvariantCulture);
            diagnostics[$"{prefix}.bottomRows"] = region.BottomCrop.ToString(CultureInfo.InvariantCulture);
        }

        return diagnostics;
    }

    private static ScrollingCaptureFailure? ValidateRequest(ScrollingStitchRequest request)
    {
        if (request.Frames.Count == 0)
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.StitchFailed,
                "Scrolling capture stitching requires at least one captured frame.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return new ScrollingCaptureFailure(
                ScrollingCaptureFailureReason.InvalidRequest,
                "Scrolling capture stitching requires an output directory.");
        }

        foreach (ScrollingCaptureFrame frame in request.Frames)
        {
            if (string.IsNullOrWhiteSpace(frame.Image.Path))
            {
                return new ScrollingCaptureFailure(
                    ScrollingCaptureFailureReason.StitchFailed,
                    "A scrolling capture frame is missing an image path.");
            }
        }

        return null;
    }

    private static double ResolveMinimumOverlapRatio(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("minimumOverlapRatio", out string? value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            && parsed > 0
            && parsed < 1)
        {
            return parsed;
        }

        return DefaultMinimumOverlapRatio;
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.None;
    }

    private sealed record OverlapMatch(
        int Rows,
        double Score,
        bool IsConfident);

    private sealed record FrameVisibleRegion(
        int FrameIndex,
        int Top,
        int Height,
        int TopCrop,
        int BottomCrop)
    {
        public static FrameVisibleRegion Create(
            int frameIndex,
            int frameHeight,
            int topCrop,
            int bottomCrop)
        {
            int clampedTop = Math.Clamp(topCrop, 0, Math.Max(0, frameHeight - 1));
            int clampedBottom = Math.Clamp(bottomCrop, 0, Math.Max(0, frameHeight - clampedTop - 1));
            int height = Math.Max(1, frameHeight - clampedTop - clampedBottom);

            return new FrameVisibleRegion(
                frameIndex,
                clampedTop,
                height,
                clampedTop,
                clampedBottom);
        }
    }

    private sealed record StitchSegment(
        int FrameIndex,
        int SourceTop,
        int Height,
        int Overlap,
        double Score,
        bool IsConfident);
}
