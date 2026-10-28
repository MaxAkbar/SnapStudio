using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Documents;
using Svg;

namespace SnapStudio.Rendering;

public static class SvgRasterizer
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<(string Svg, int Width, int Height), AnnotationImage> Cache = [];
    private static readonly Queue<(string Svg, int Width, int Height)> CacheOrder = [];
    private static long _cacheBytes;

    static SvgRasterizer()
    {
        SvgDocument.DisableDtdProcessing = true;
        SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
        SvgDocument.ResolveExternalElements = ExternalType.None;
        SvgDocument.ResolveExternalImages = ExternalType.None;
    }

    public static AnnotationImage Preview(string svg, double width, double height)
    {
        double scale = Math.Min(1, 4096 / Math.Max(1, Math.Max(width, height)));
        int w = Math.Max(1, (int)Math.Ceiling(width * scale)), h = Math.Max(1, (int)Math.Ceiling(height * scale));
        var key = (svg, w, h);
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out AnnotationImage? cached)) return cached;
        }
        using Bitmap bitmap = Render(svg, w, h);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        var result = new AnnotationImage(w, h, stream.ToArray());
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out AnnotationImage? existing)) return existing;
            while (CacheOrder.Count > 0 && (Cache.Count >= 64 || _cacheBytes + result.PngBytes.Length > 32 * 1024 * 1024))
                if (Cache.Remove(CacheOrder.Dequeue(), out AnnotationImage? removed)) _cacheBytes -= removed.PngBytes.Length;
            if (result.PngBytes.Length <= 32 * 1024 * 1024) { Cache[key] = result; CacheOrder.Enqueue(key); _cacheBytes += result.PngBytes.Length; }
        }
        return result;
    }

    public static Bitmap Render(string svg, int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        SvgDocument document = SvgDocument.FromSvg<SvgDocument>(SvgPatternBaker.Bake(SvgShape.Normalize(svg)));
        document.Width = new SvgUnit(width); document.Height = new SvgUnit(height);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        try { using Graphics graphics = Graphics.FromImage(bitmap); document.Draw(graphics); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    public static void Draw(Graphics graphics, string svg, RectangleF bounds, double opacity)
    {
        using Bitmap bitmap = Render(svg, Math.Max(1, (int)Math.Ceiling(bounds.Width)), Math.Max(1, (int)Math.Ceiling(bounds.Height)));
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)Math.Clamp(opacity, 0, 1) });
        graphics.DrawImage(bitmap, [new(bounds.Left, bounds.Top), new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Bottom)],
            new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel, attributes);
    }
}
