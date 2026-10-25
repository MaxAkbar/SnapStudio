using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Rendering;

internal static class SystemDrawingRegionFill
{
    public static RegionFillResult Find(RegionFillRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(request.Position.X) || !double.IsFinite(request.Position.Y)
            || request.Tolerance is < 0 or > 255)
        {
            return RegionFillResult.Failed("Choose a point on the image and a tolerance from 0 to 255.");
        }

        using var stream = new MemoryStream(request.Image.Pixels);
        using var source = new Bitmap(stream);
        int width = source.Width;
        int height = source.Height;
        if (request.Position.X < 0 || request.Position.Y < 0 || request.Position.X >= width || request.Position.Y >= height)
        {
            return RegionFillResult.Failed("Choose a point inside the image.");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        byte[] pixels = new byte[checked(width * height * 4)];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * width * 4, width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        int seedX = (int)request.Position.X;
        int seedY = (int)request.Position.Y;
        int seed = (seedY * width + seedX) * 4;
        var original = new ColorRgba(pixels[seed + 2], pixels[seed + 1], pixels[seed], pixels[seed + 3]);
        var visited = new BitArray(width * height);
        var pending = new Stack<(int X, int Y)>();
        var spans = new List<RectD>();
        pending.Push((seedX, seedY));
        int minX = width, minY = height, maxX = 0, maxY = 0;

        bool Matches(int x, int y)
        {
            int pixel = y * width + x;
            if (visited[pixel]) return false;
            int offset = pixel * 4;
            int alpha = pixels[offset + 3];
            if (Math.Abs(alpha - original.A) > request.Tolerance) return false;
            // Invisible RGB values do not separate two transparent areas.
            return (alpha <= request.Tolerance && original.A <= request.Tolerance)
                || (Math.Abs(pixels[offset + 2] - original.R) <= request.Tolerance
                    && Math.Abs(pixels[offset + 1] - original.G) <= request.Tolerance
                    && Math.Abs(pixels[offset] - original.B) <= request.Tolerance);
        }

        while (pending.TryPop(out var point))
        {
            if (!request.Contiguous) break;
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(point.X, point.Y)) continue;
            int left = point.X;
            int right = point.X;
            while (left > 0 && Matches(left - 1, point.Y)) left--;
            while (right < width - 1 && Matches(right + 1, point.Y)) right++;
            for (int x = left; x <= right; x++) visited[point.Y * width + x] = true;
            spans.Add(new RectD(left, point.Y, right - left + 1, 1));
            minX = Math.Min(minX, left); minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, right + 1); maxY = Math.Max(maxY, point.Y + 1);
            foreach (int y in new[] { point.Y - 1, point.Y + 1 })
            {
                if (y < 0 || y >= height) continue;
                bool inSpan = false;
                for (int x = left; x <= right; x++)
                {
                    bool matches = Matches(x, y);
                    if (matches && !inSpan) pending.Push((x, y));
                    inSpan = matches;
                }
            }
        }

        if (!request.Contiguous)
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int start = -1;
                for (int x = 0; x <= width; x++)
                {
                    bool matches = x < width && Matches(x, y);
                    if (matches && start < 0) start = x;
                    else if (!matches && start >= 0)
                    {
                        spans.Add(new(start, y, x - start, 1));
                        minX = Math.Min(minX, start); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y + 1);
                        start = -1;
                    }
                }
            }
        }

        var rectangles = new List<RectD>();
        var lastBySpan = new Dictionary<(double X, double Width), int>();
        foreach (RectD span in spans.OrderBy(span => span.Y).ThenBy(span => span.X))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (span.X, span.Width);
            if (lastBySpan.TryGetValue(key, out int index) && rectangles[index].Bottom == span.Y)
                rectangles[index] = rectangles[index] with { Height = rectangles[index].Height + 1 };
            else
            {
                lastBySpan[key] = rectangles.Count;
                rectangles.Add(span);
            }
        }

        var bounds = new RectD(minX, minY, maxX - minX, maxY - minY);
        RectD[] normalized = rectangles.Select(rectangle => new RectD((rectangle.X - minX) / bounds.Width,
            (rectangle.Y - minY) / bounds.Height, rectangle.Width / bounds.Width, rectangle.Height / bounds.Height)).ToArray();
        return new RegionFillResult(new FillRegion(bounds, normalized, original), null);
    }
}
