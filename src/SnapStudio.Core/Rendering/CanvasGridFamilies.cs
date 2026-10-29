using System.Globalization;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Rendering;

public enum GridGuideFamily { Grid, Center, Left, Right, Top, Bottom }

public sealed record GridGuideSettings(string Color, double Opacity = 75, int Density = 12)
{
    internal GridGuideSettings Normalize(GridGuideSettings fallback)
    {
        string? hex = Color?.Trim();
        string color = hex is { Length: 7 } && hex[0] == '#'
            && uint.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)
            ? $"#{rgb:X6}" : fallback.Color;
        return new(color, double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 100) : fallback.Opacity, Math.Clamp(Density, 4, 32));
    }

    public ColorRgba GetColor()
    {
        var style = Normalize(new("#64748B"));
        uint rgb = uint.Parse(style.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)Math.Round(style.Opacity * 255 / 100, MidpointRounding.AwayFromZero));
    }

    public ColorRgba GetContrastColor()
    {
        var color = GetColor();
        return (299 * color.R + 587 * color.G + 114 * color.B >= 150000 ? ColorRgba.Black : ColorRgba.White)
            with
        { A = (byte)Math.Round(color.A * .55, MidpointRounding.AwayFromZero) };
    }
}

public sealed record CanvasGridFamilies(GridGuideSettings? Grid = null, GridGuideSettings? Center = null,
    GridGuideSettings? Left = null, GridGuideSettings? Right = null, GridGuideSettings? Top = null, GridGuideSettings? Bottom = null)
{
    public GridGuideSettings Get(GridGuideFamily family, int defaultDensity = 12)
    {
        var fallback = family switch
        {
            GridGuideFamily.Center => new GridGuideSettings("#7C3AED", 75, defaultDensity),
            GridGuideFamily.Left => new("#E05252", 75, defaultDensity),
            GridGuideFamily.Right => new("#0E9F6E", 75, defaultDensity),
            GridGuideFamily.Top => new("#2474D9", 75, defaultDensity),
            GridGuideFamily.Bottom => new("#D18A00", 75, defaultDensity),
            _ => new("#64748B", 45, defaultDensity)
        };
        return ((family switch
        {
            GridGuideFamily.Center => Center,
            GridGuideFamily.Left => Left,
            GridGuideFamily.Right => Right,
            GridGuideFamily.Top => Top,
            GridGuideFamily.Bottom => Bottom,
            _ => Grid
        }) ?? fallback).Normalize(fallback);
    }

    public CanvasGridFamilies Normalize(int defaultDensity = 12) => new(Get(GridGuideFamily.Grid, defaultDensity), Get(GridGuideFamily.Center, defaultDensity),
        Get(GridGuideFamily.Left, defaultDensity), Get(GridGuideFamily.Right, defaultDensity), Get(GridGuideFamily.Top, defaultDensity), Get(GridGuideFamily.Bottom, defaultDensity));

    public static IReadOnlyList<GridGuideFamily> Active(CanvasGridMode mode) => mode switch
    {
        CanvasGridMode.OnePoint => [GridGuideFamily.Grid, GridGuideFamily.Center],
        CanvasGridMode.TwoPoint => [GridGuideFamily.Grid, GridGuideFamily.Left, GridGuideFamily.Right],
        CanvasGridMode.ThreePoint => [GridGuideFamily.Grid, GridGuideFamily.Left, GridGuideFamily.Right, GridGuideFamily.Top],
        CanvasGridMode.FourPoint => [GridGuideFamily.Grid, GridGuideFamily.Left, GridGuideFamily.Right, GridGuideFamily.Top, GridGuideFamily.Bottom],
        CanvasGridMode.FivePoint => [GridGuideFamily.Grid, GridGuideFamily.Left, GridGuideFamily.Right, GridGuideFamily.Top, GridGuideFamily.Bottom, GridGuideFamily.Center],
        _ => [GridGuideFamily.Grid]
    };
}
