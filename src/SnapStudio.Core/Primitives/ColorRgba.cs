namespace SnapStudio.Core.Primitives;

public readonly record struct ColorRgba(byte R, byte G, byte B, byte A)
{
    public static ColorRgba Transparent => new(0, 0, 0, 0);

    public static ColorRgba Black => new(0, 0, 0, 255);

    public static ColorRgba White => new(255, 255, 255, 255);
}
