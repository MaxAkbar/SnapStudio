using System.Text.Json;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveSnapTargetTests
{
    private static readonly RectD View = new(0, 0, 400, 400);

    [TestMethod]
    [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public void FilteringRestrictsFamiliesWithoutChangingDisplayedGuides(int mode)
    {
        var settings = new PerspectiveGridSettings((CanvasGridMode)mode, Radius: 40);
        var visible = PerspectiveGrid.Create(View, settings);
        foreach (var family in Enum.GetValues<GridGuideFamily>())
        {
            var filtered = PerspectiveTargetFilter.For(family).Filter(visible);
            Assert.IsTrue(filtered.All(stroke => stroke.Family == family));
            Assert.AreEqual(visible.Count(stroke => stroke.Family == family), filtered.Count);
        }
        Assert.AreSame(visible, PerspectiveSnapTargets.All.Filter(visible));
        Assert.HasCount(0, PerspectiveSnapTargets.None.Filter(visible));
        Assert.AreEqual(mode + 1, visible.Select(stroke => stroke.Family).Distinct().Count());
    }

    [TestMethod]
    [DataRow(PerspectiveSnapTargets.Left, PerspectiveLineFamily.FirstVanishingPoint)]
    [DataRow(PerspectiveSnapTargets.Right, PerspectiveLineFamily.SecondVanishingPoint)]
    [DataRow(PerspectiveSnapTargets.Grid, PerspectiveLineFamily.Vertical)]
    public void ExplicitTargetsOverrideTheNearestDirectionAndAnExcludedLock(PerspectiveSnapTargets targets, PerspectiveLineFamily expected)
    {
        var line = PerspectiveLineDrawing.Constrain(new(180, 120), new(330, 245), View, new(CanvasGridMode.TwoPoint), locked: PerspectiveLineFamily.SecondVanishingPoint, targets: targets);
        Assert.AreEqual(expected, line.Direction);
        Assert.HasCount(1, line.Guides);
        Assert.IsTrue(targets.Allows(line.Direction!.Value, CanvasGridMode.TwoPoint));
    }

    [TestMethod]
    public void NoneOrUnavailableTargetsDrawFreely()
    {
        var start = new PointD(180, 120); var end = new PointD(330, 245);
        foreach (var targets in new[] { PerspectiveSnapTargets.None, PerspectiveSnapTargets.Bottom, PerspectiveSnapTargets.Center })
        {
            var line = PerspectiveLineDrawing.Constrain(start, end, View, new(CanvasGridMode.TwoPoint), targets: targets);
            Assert.AreEqual(end, line.End); Assert.IsNull(line.Direction); Assert.HasCount(0, line.Guides);
        }
    }

    [TestMethod]
    public void CurvedPolesShareTheirGeometricFamilyAndCenterRemainsSeparate()
    {
        var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint);
        foreach (var targets in new[] { PerspectiveSnapTargets.Top, PerspectiveSnapTargets.Bottom })
        {
            var curve = PerspectiveCurveDrawing.Draw(new(100, 120), new(135, 290), View, settings, targets: targets);
            Assert.AreEqual(PerspectiveLineFamily.CurvedVertical, curve.Direction); Assert.IsTrue(curve.Points.Count > 2);
        }
        foreach (var targets in new[] { PerspectiveSnapTargets.Left, PerspectiveSnapTargets.Right })
            Assert.AreEqual(PerspectiveLineFamily.CurvedHorizontal, PerspectiveCurveDrawing.Draw(new(100, 120), new(290, 135), View, settings, targets: targets).Direction);
        Assert.AreEqual(PerspectiveLineFamily.CenterVanishingPoint, PerspectiveCurveDrawing.Draw(new(100, 120), new(290, 135), View, settings, targets: PerspectiveSnapTargets.Center).Direction);
    }

    [TestMethod]
    public void RestrictionsPersistAndLegacyDocumentsDefaultToAll()
    {
        var style = AnnotationStyle.Default with { PerspectiveTargets = PerspectiveSnapTargets.Left | PerspectiveSnapTargets.Top };
        Assert.AreEqual(style, JsonSerializer.Deserialize<AnnotationStyle>(JsonSerializer.Serialize(style)));
        Assert.AreEqual(PerspectiveSnapTargets.All, JsonSerializer.Deserialize<AnnotationStyle>("{}")!.PerspectiveTargets);
        var legacy = new CaptureDocument { SchemaVersion = 10 }; DocumentLayers.EnsureInitialized(legacy);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, legacy.SchemaVersion);
    }

    [TestMethod]
    public async Task MultiObjectEditUndoRedoKeepIndependentTargets()
    {
        var document = new CaptureDocument(); var layer = document.Layers[0];
        var a = new AnnotationObject { Kind = AnnotationKind.Rectangle, Bounds = new(10, 10, 20, 20), LayerId = layer.Id };
        var b = new AnnotationObject { Kind = AnnotationKind.Rectangle, Bounds = new(40, 40, 20, 20), LayerId = layer.Id, Style = AnnotationStyle.Default with { PerspectiveTargets = PerspectiveSnapTargets.Right } };
        document.Annotations.AddRange([a, b]); var stack = new EditCommandStack(); var editor = new DocumentSelectionEditor(stack);
        await editor.SetStylesAsync(document, new Dictionary<Guid, AnnotationStyle> { [a.Id] = a.Style with { PerspectiveTargets = PerspectiveSnapTargets.Left }, [b.Id] = b.Style with { PerspectiveTargets = PerspectiveSnapTargets.Left } }, default);
        Assert.AreEqual(PerspectiveSnapTargets.Left, a.Style.PerspectiveTargets); Assert.AreEqual(PerspectiveSnapTargets.Left, b.Style.PerspectiveTargets);
        await stack.UndoAsync(document, default); Assert.AreEqual(PerspectiveSnapTargets.All, a.Style.PerspectiveTargets); Assert.AreEqual(PerspectiveSnapTargets.Right, b.Style.PerspectiveTargets);
        await stack.RedoAsync(document, default); Assert.AreEqual(PerspectiveSnapTargets.Left, b.Style.PerspectiveTargets);
    }

    [TestMethod]
    public void BoundMovesUseChangedTargetsAndPreserveTheOriginalObject()
    {
        var settings = new PerspectiveGridSettings(CanvasGridMode.TwoPoint);
        var original = PerspectiveCurveDrawing.Draw(new(130, 120), new(260, 260), View, settings, locked: PerspectiveLineFamily.SecondVanishingPoint).ToAnnotation(AnnotationStyle.Default with { PerspectiveTargets = PerspectiveSnapTargets.Left });
        string before = original.Vector!.Svg;
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, original.Bounds with { X = original.Bounds.X + 10 }, View, settings, 1, 1, out var moved));
        Assert.IsNotNull(moved); var path = XDocument.Parse(moved.Vector!.Svg).Descendants().Single(el => el.Name.LocalName == "path");
        Assert.AreEqual("2", (string?)path.Attribute("data-perspective-family")); Assert.AreEqual(before, original.Vector.Svg);
        original.Style = original.Style with { PerspectiveTargets = PerspectiveSnapTargets.None };
        Assert.IsFalse(PerspectiveObjectBinding.TryReproject(original, original.Bounds, View, settings, 1, 1, out _));
    }

    [TestMethod]
    public void SvgGroupRestrictionsIntersectAndInvalidMasksAreIgnored()
    {
        var root = XElement.Parse("<svg data-snap-targets='12'><g data-snap-targets='4'><path data-snap-targets='63'/></g></svg>");
        var path = root.Descendants().Last(); Assert.AreEqual(PerspectiveSnapTargets.Left, PerspectiveTargetFilter.FromSvg(path));
        path.SetAttributeValue("data-snap-targets", 8); Assert.AreEqual(PerspectiveSnapTargets.None, PerspectiveTargetFilter.FromSvg(path));
        path.SetAttributeValue("data-snap-targets", -1); Assert.AreEqual(PerspectiveSnapTargets.Left, PerspectiveTargetFilter.FromSvg(path));
        Assert.Throws<InvalidDataException>(() => PerspectiveTargetFilter.Validate((PerspectiveSnapTargets)64));
    }
}
