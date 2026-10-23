using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsCursorSnapshot(AnnotationImage Image, RectD ScreenBounds, nint RootWindow)
{
    public CapturedCursor? ForCapture(RectD screenBounds, nint window = 0)
    {
        if ((window != 0 && RootWindow != window)
            || ScreenBounds.Right <= screenBounds.X || ScreenBounds.X >= screenBounds.Right
            || ScreenBounds.Bottom <= screenBounds.Y || ScreenBounds.Y >= screenBounds.Bottom) return null;
        // Keep the full image at the edge; clipping must not stretch the pointer.
        return new CapturedCursor(Image, ScreenBounds with { X = ScreenBounds.X - screenBounds.X, Y = ScreenBounds.Y - screenBounds.Y });
    }
}

public static class WindowsCursorCapture
{
    public static WindowsCursorSnapshot? Capture()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((info.Flags & 1) == 0 || info.Cursor == 0) return null;
        nint copy = CopyIcon(info.Cursor);
        if (copy == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetIconInfo(copy, out IconInfo icon)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                using Bitmap resource = System.Drawing.Image.FromHbitmap(icon.Color != 0 ? icon.Color : icon.Mask);
                int width = resource.Width;
                int height = icon.Color != 0 ? resource.Height : resource.Height / 2;
                var bounds = new RectD(info.Position.X - icon.HotspotX, info.Position.Y - icon.HotspotY, width, height);
                using Bitmap black = DrawOn(copy, width, height, Color.Black);
                using Bitmap white = DrawOn(copy, width, height, Color.White);
                using Bitmap transparent = ExtractTransparency(black, white, () => DrawOnScreen(copy, bounds));
                using var stream = new MemoryStream();
                transparent.Save(stream, ImageFormat.Png);
                return new WindowsCursorSnapshot(new AnnotationImage(width, height, stream.ToArray()), bounds,
                    GetAncestor(WindowFromPoint(info.Position), 2));
            }
            finally
            {
                if (icon.Color != 0) DeleteObject(icon.Color);
                if (icon.Mask != 0) DeleteObject(icon.Mask);
            }
        }
        finally { DestroyIcon(copy); }
    }

    // Drawing over black and white recovers both alpha cursors and legacy AND/XOR masks.
    // XOR inversion cannot be represented by PNG alpha; freeze those pixels against
    // their captured backdrop while keeping the rest of the image transparent.
    public static Bitmap ExtractTransparency(Bitmap black, Bitmap white, Func<Bitmap> getInvertedBackdrop)
    {
        var output = new Bitmap(black.Width, black.Height, PixelFormat.Format32bppArgb);
        Bitmap? inverted = null;
        try
        {
            for (int y = 0; y < black.Height; y++)
                for (int x = 0; x < black.Width; x++)
                {
                    Color b = black.GetPixel(x, y), w = white.GetPixel(x, y);
                    if (w.R < b.R || w.G < b.G || w.B < b.B)
                    {
                        inverted ??= getInvertedBackdrop();
                        Color pixel = inverted.GetPixel(x, y);
                        output.SetPixel(x, y, Color.FromArgb(255, pixel.R, pixel.G, pixel.B));
                        continue;
                    }
                    int alpha = 255 - Math.Max(w.R - b.R, Math.Max(w.G - b.G, w.B - b.B));
                    if (alpha == 0) continue;
                    output.SetPixel(x, y, Color.FromArgb(alpha, Math.Min(255, b.R * 255 / alpha),
                        Math.Min(255, b.G * 255 / alpha), Math.Min(255, b.B * 255 / alpha)));
                }
            return output;
        }
        catch { output.Dispose(); throw; }
        finally { inverted?.Dispose(); }
    }

    private static Bitmap DrawOn(nint cursor, int width, int height, Color background)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(background);
            Draw(graphics, cursor, width, height);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static Bitmap DrawOnScreen(nint cursor, RectD bounds)
    {
        var bitmap = new Bitmap((int)bounds.Width, (int)bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, bitmap.Size);
            Draw(graphics, cursor, bitmap.Width, bitmap.Height);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static void Draw(Graphics graphics, nint cursor, int width, int height)
    {
        nint dc = graphics.GetHdc();
        try { if (!DrawIconEx(dc, 0, 0, cursor, width, height, 0, 0, 3)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        finally { graphics.ReleaseHdc(dc); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size; public int Flags; public nint Cursor; public NativePoint Position; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public int IsIcon; public uint HotspotX; public uint HotspotY; public nint Mask; public nint Color; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint frame, nint brush, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}
