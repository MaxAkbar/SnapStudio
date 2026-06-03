namespace SnapStudio.Core.Primitives;

public readonly record struct PointD(double X, double Y);

public readonly record struct SizeD(double Width, double Height);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}
