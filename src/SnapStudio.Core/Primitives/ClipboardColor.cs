using System.Globalization;

namespace SnapStudio.Core.Primitives;

public static class ClipboardColor
{
    /// <summary>Accepts opaque RGB hex colors, including the default PowerToys Color Picker format.</summary>
    public static bool TryParse(string? text, out ColorRgba color)
    {
        color = default;
        if (text is null || text.Length > 32) return false;
        ReadOnlySpan<char> value = text.AsSpan().Trim();
        if (value.StartsWith("#")) value = value[1..];
        if (value.Length is not (3 or 6) || !uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
            return false;
        if (value.Length == 3)
            rgb = ((rgb & 0xF00) << 12) | ((rgb & 0xF00) << 8)
                | ((rgb & 0x0F0) << 8) | ((rgb & 0x0F0) << 4) | ((rgb & 0x00F) << 4) | (rgb & 0x00F);
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        return true;
    }
}
