using SnapStudio.Core.Rendering;
using System.Globalization;
using System.Xml.Linq;

namespace SnapStudio.Core.Documents;

[Flags]
public enum PerspectiveSnapTargets { None = 0, Grid = 1, Center = 2, Left = 4, Right = 8, Top = 16, Bottom = 32, All = 63 }

public static class PerspectiveTargetFilter
{
    public static PerspectiveSnapTargets For(GridGuideFamily family) => (PerspectiveSnapTargets)(1 << (int)family);
    public static bool Allows(this PerspectiveSnapTargets targets, GridGuideFamily family) => (targets & For(family)) != 0;
    public static void Validate(PerspectiveSnapTargets targets)
    {
        if ((targets & ~PerspectiveSnapTargets.All) != 0) throw new InvalidDataException("The perspective snap targets are invalid.");
    }
    public static PerspectiveSnapTargets FromSvg(XElement element)
    {
        var targets = PerspectiveSnapTargets.All;
        foreach (var ancestor in element.AncestorsAndSelf())
            if (int.TryParse((string?)ancestor.Attribute("data-snap-targets"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value is >= 0 and <= 63)
                targets &= (PerspectiveSnapTargets)value;
        return targets;
    }
    public static IReadOnlyList<PerspectiveGridStroke> Filter(this PerspectiveSnapTargets targets, IReadOnlyList<PerspectiveGridStroke> strokes) =>
        targets == PerspectiveSnapTargets.All ? strokes : strokes.Where(stroke => targets.Allows(stroke.Family)).ToArray();
    public static bool Allows(this PerspectiveSnapTargets targets, PerspectiveLineFamily family, CanvasGridMode mode) => family switch
    {
        PerspectiveLineFamily.Horizontal or PerspectiveLineFamily.Vertical => targets.Allows(GridGuideFamily.Grid),
        PerspectiveLineFamily.FirstVanishingPoint => targets.Allows(mode == CanvasGridMode.OnePoint ? GridGuideFamily.Center : GridGuideFamily.Left),
        PerspectiveLineFamily.SecondVanishingPoint => targets.Allows(GridGuideFamily.Right),
        PerspectiveLineFamily.ThirdVanishingPoint => targets.Allows(GridGuideFamily.Top),
        PerspectiveLineFamily.CenterVanishingPoint => targets.Allows(GridGuideFamily.Center),
        PerspectiveLineFamily.CurvedHorizontal => (targets & (PerspectiveSnapTargets.Left | PerspectiveSnapTargets.Right)) != 0,
        PerspectiveLineFamily.CurvedVertical => (targets & (PerspectiveSnapTargets.Top | PerspectiveSnapTargets.Bottom)) != 0,
        _ => false
    };
}
