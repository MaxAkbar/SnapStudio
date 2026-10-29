using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveObjectBindingTests
{
    private static readonly RectD View = new(0, 0, 256, 256);
    private static XElement Path(AnnotationObject item) => XDocument.Parse(item.Vector!.Svg).Root!.Elements().Single(e => e.Name.LocalName == "path");
    private static PointD Point(XElement path, string name)
    {
        var n = path.Attribute(name)!.Value.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return new(n[0], n[1]);
    }
    private static PointD[] Points(XElement path)
    {
        var n = Regex.Matches(path.Attribute("d")!.Value, @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        return Enumerable.Range(0, n.Length / 2).Select(i => new PointD(n[i * 2], n[i * 2 + 1])).ToArray();
    }
    private static RectD Moved(AnnotationObject item, double x = 12, double y = 9) => item.Bounds with { X = item.Bounds.X + x, Y = item.Bounds.Y + y };
    private static AnnotationObject Line(int mode = 5, PerspectiveLineFamily family = PerspectiveLineFamily.CurvedVertical) => PerspectiveCurveDrawing.Draw(new(70, 70), new(95, 170), View,
        new((CanvasGridMode)mode), locked: family).ToAnnotation(AnnotationStyle.Default);
    private static AnnotationObject StripBinding(AnnotationObject item)
    {
        var doc = XDocument.Parse(item.Vector!.Svg);
        foreach (var attribute in doc.Descendants().Attributes().Where(a => a.Name.LocalName.StartsWith("data-perspective-", StringComparison.Ordinal)).ToArray()) attribute.Remove();
        item.Vector = item.Vector with { Svg = doc.ToString(SaveOptions.DisableFormatting) }; return item;
    }

    [TestMethod]
    [DataRow(4, PerspectiveLineFamily.CurvedVertical)]
    [DataRow(5, PerspectiveLineFamily.CurvedVertical)]
    [DataRow(5, PerspectiveLineFamily.CurvedHorizontal)]
    public void MovingALineRebuildsTheCircularFamilyThroughItsNewStart(int mode, PerspectiveLineFamily family)
    {
        var original = Line(mode, family); var before = Path(original); var start = Point(before, "data-perspective-start");
        Assert.IsTrue(PerspectiveObjectBinding.IsPerspectiveObject(original));
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original), View, new((CanvasGridMode)mode), 1, 1, out var moved)); Assert.IsNotNull(moved);
        var path = Path(moved); PointD a = Point(path, "data-perspective-start");
        Assert.AreEqual(start.X + 12, a.X, 1e-5); Assert.AreEqual(start.Y + 9, a.Y, 1e-5);
        Assert.AreEqual((int)family, int.Parse(path.Attribute("data-perspective-family")!.Value));
        Assert.AreNotEqual(before.Attribute("d")!.Value, path.Attribute("d")!.Value);
        Assert.AreEqual(path.Attribute("d")!.Value, path.Attribute("data-perspective-path")!.Value);
        double radius = 256 * .7, x = (a.X - 128) / radius, y = (a.Y - 128) / radius;
        foreach (var p in Points(path))
        {
            double px = (p.X - 128) / radius, py = (p.Y - 128) / radius;
            double residual = family == PerspectiveLineFamily.CurvedVertical
                ? x * (px * px + py * py - 1) + (1 - x * x - y * y) * px
                : y * (px * px + py * py - 1) + (1 - x * x - y * y) * py;
            Assert.AreEqual(0, residual, 1e-7);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void MovingAStraightPerspectiveLineStillConvergesToTheSameGridFamily(int mode)
    {
        var family = mode == 1 ? PerspectiveLineFamily.FirstVanishingPoint : PerspectiveLineFamily.SecondVanishingPoint;
        var original = Line(mode, family);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original), View, new((CanvasGridMode)mode), 2, .6, out var moved)); Assert.IsNotNull(moved);
        var path = Path(moved); var points = Points(path); Assert.HasCount(2, points);
        PointD vp = mode == 1 ? new(128, 128) : new(384, 128);
        Assert.AreEqual(0, (points[1].X - points[0].X) * (vp.Y - points[0].Y) - (points[1].Y - points[0].Y) * (vp.X - points[0].X), .001);
    }

    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 0)]
    [DataRow(3, 1)]
    [DataRow(3, 2)]
    [DataRow(4, 0)]
    [DataRow(4, 1)]
    [DataRow(4, 2)]
    [DataRow(5, 0)]
    [DataRow(5, 1)]
    [DataRow(5, 2)]
    public void RectangleMovesRetainTheSelectedPlaneAndRecreateItsEdges(int mode, int plane)
    {
        PointD a = new(45, 45), c = plane == 2 && mode is >= 2 and <= 4 ? new(160, 100) : new(95, 85);
        if (plane == 2 && mode is >= 2 and <= 4) a = new(100, 60);
        var original = PerspectiveRectangle.Create(a, c, View, new((CanvasGridMode)mode), (PerspectiveRectanglePlane)plane)!.ToAnnotation(AnnotationStyle.Default);
        Assert.AreEqual(plane.ToString(CultureInfo.InvariantCulture), Path(original).Attribute("data-perspective-plane")!.Value);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original, 6, 5), View, new((CanvasGridMode)mode), 1, 1, out var moved)); Assert.IsNotNull(moved);
        var path = Path(moved); var start = Point(path, "data-perspective-start"); var end = Point(path, "data-perspective-end");
        Assert.AreEqual(a.X + 6, start.X, 1e-5); Assert.AreEqual(a.Y + 5, start.Y, 1e-5);
        var expected = PerspectiveRectangle.Create(start, end, View, new((CanvasGridMode)mode), (PerspectiveRectanglePlane)plane)!;
        Assert.AreEqual(plane.ToString(CultureInfo.InvariantCulture), path.Attribute("data-perspective-plane")!.Value);
        var actual = Points(path); Assert.AreEqual(expected.Outline.Count - 1, actual.Length);
        for (int i = 0; i < actual.Length; i++) { Assert.AreEqual(expected.Outline[i].X, actual[i].X, 2e-5); Assert.AreEqual(expected.Outline[i].Y, actual[i].Y, 2e-5); }
    }

    [TestMethod]
    public void BindingSurvivesSaveAndAnEarlierFreeAffineMove()
    {
        var original = Line(); original.Bounds = Moved(original, 10, 4);
        original = JsonSerializer.Deserialize<AnnotationObject>(JsonSerializer.Serialize(original))!;
        original.Vector = original.Vector! with { Svg = SvgShape.Normalize(original.Vector.Svg) };
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original, 5, 6), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved)); Assert.IsNotNull(moved);
        var start = Point(Path(moved), "data-perspective-start");
        Assert.AreEqual(85, start.X, 1e-5); Assert.AreEqual(80, start.Y, 1e-5);
        var again = JsonSerializer.Deserialize<AnnotationObject>(JsonSerializer.Serialize(moved))!;
        Assert.IsTrue(PerspectiveObjectBinding.IsPerspectiveObject(again));
    }

    [TestMethod]
    public void MatrixTransformedSvgAnchorsMapThroughTheViewBoxIntoTheCurrentObjectBounds()
    {
        var original = Line(); var doc = XDocument.Parse(original.Vector!.Svg); var path = doc.Root!.Elements().Single();
        path.SetAttributeValue("transform", "matrix(1 0 0 1 8 3)"); original.Vector = original.Vector with { Svg = doc.ToString(SaveOptions.DisableFormatting) };
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original, 5, 6), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved)); Assert.IsNotNull(moved);
        var start = Point(Path(moved), "data-perspective-start");
        Assert.AreEqual(83, start.X, 1e-5); Assert.AreEqual(79, start.Y, 1e-5); Assert.IsNull(Path(moved).Attribute("transform"));
    }

    [TestMethod]
    public void SvgPaintEditsAttributionLayerVisibilityAndOpacityRemainIntact()
    {
        var original = Line(); original.LayerId = Guid.NewGuid(); original.IsVisible = false;
        original.Style = original.Style with { Opacity = .37 };
        original.Vector = original.Vector! with { Attribution = new("test", "test:shape", "test", "author", "license", "https://example.test/license", "https://example.test/source") };
        var doc = XDocument.Parse(original.Vector.Svg); var path = doc.Root!.Elements().Single();
        path.SetAttributeValue("stroke", "#E5173D"); path.SetAttributeValue("style", "stroke-width: 12; stroke-dasharray: 48 24; opacity: .4");
        doc.Root.AddFirst(new XElement(doc.Root.Name.Namespace + "style", "path {stroke-linecap: square}"));
        original.Vector = original.Vector with { Svg = doc.ToString(SaveOptions.DisableFormatting) };
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved)); Assert.IsNotNull(moved);
        Assert.AreEqual(original.Id, moved.Id); Assert.AreEqual(original.LayerId, moved.LayerId); Assert.IsFalse(moved.IsVisible);
        Assert.AreEqual(original.Style, moved.Style); Assert.AreEqual(original.Vector.Attribution, moved.Vector!.Attribution);
        Assert.AreEqual("#E5173D", Path(moved).Attribute("stroke")!.Value); Assert.AreEqual(path.Attribute("style")!.Value, Path(moved).Attribute("style")!.Value);
        Assert.IsTrue(moved.Vector.Svg.Contains("path {stroke-linecap: square}"));
        Assert.IsTrue(moved.Bounds.Width >= Points(Path(moved)).Max(p => p.X) - Points(Path(moved)).Min(p => p.X) + 13.99);
    }

    [TestMethod]
    [DataRow("line")]
    [DataRow("rectangle")]
    public void LegacyGeneratedObjectsAcquireBindingsEvenAfterAnEarlierAffineMove(string tool)
    {
        var original = tool == "line" ? Line() : PerspectiveRectangle.Create(new(70, 70), new(120, 100), View, new(CanvasGridMode.FivePoint), PerspectiveRectanglePlane.Front)!.ToAnnotation(AnnotationStyle.Default);
        original = StripBinding(original); original.Bounds = Moved(original, 4, 3);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original, 5, 6), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved)); Assert.IsNotNull(moved);
        var start = Point(Path(moved), "data-perspective-start"); Assert.AreEqual(79, start.X, 1e-4); Assert.AreEqual(79, start.Y, 1e-4);
        Assert.AreEqual(tool, Path(moved).Attribute("data-perspective-tool")!.Value);
    }

    [TestMethod]
    public void NodeEditsMalformedMetadataAndUnrelatedSvgAreNotOverwritten()
    {
        foreach (string edit in new[] { "geometry", "start", "family", "group", "transform" })
        {
            var original = Line(); var doc = XDocument.Parse(original.Vector!.Svg); var path = doc.Root!.Elements().Single();
            if (edit == "geometry") path.SetAttributeValue("d", "M70 70 L140 120");
            if (edit == "start") path.SetAttributeValue("data-perspective-start", "NaN,70");
            if (edit == "family") path.SetAttributeValue("data-perspective-family", "999");
            if (edit == "transform") path.SetAttributeValue("transform", "translate(10 20)");
            if (edit == "group") { path.Remove(); doc.Root.Add(new XElement(doc.Root.Name.Namespace + "g", path)); }
            original.Vector = original.Vector with { Svg = doc.ToString(SaveOptions.DisableFormatting) };
            Assert.IsFalse(PerspectiveObjectBinding.TryReproject(original, Moved(original), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved), edit); Assert.IsNull(moved);
        }
        var unrelated = StripBinding(Line()); unrelated.Vector = unrelated.Vector! with { Name = "My drawing" };
        Assert.IsFalse(PerspectiveObjectBinding.IsPerspectiveObject(unrelated));
        Assert.IsFalse(PerspectiveObjectBinding.TryReproject(unrelated, Moved(unrelated), View, new(CanvasGridMode.FivePoint), 1, 1, out _));
    }

    [TestMethod]
    public void CssGeometryTransformsDetachWhilePaintOnlyStylesRemainBindable()
    {
        foreach (string css in new[] { "path {transform: translateX(10px)}", "path {d: path('M0 0 L10 10')}", "path {transform/**/: scale(2)}" })
        {
            var item = Line(); var doc = XDocument.Parse(item.Vector!.Svg);
            doc.Root!.AddFirst(new XElement(doc.Root.Name.Namespace + "style", css));
            item.Vector = item.Vector with { Svg = doc.ToString(SaveOptions.DisableFormatting) };
            Assert.IsFalse(PerspectiveObjectBinding.TryReproject(item, Moved(item), View, new(CanvasGridMode.FivePoint), 1, 1, out _));
        }
        var rootStyle = Line(); var rootDoc = XDocument.Parse(rootStyle.Vector!.Svg); rootDoc.Root!.SetAttributeValue("style", "transform:scale(2)");
        rootStyle.Vector = rootStyle.Vector with { Svg = rootDoc.ToString(SaveOptions.DisableFormatting) };
        Assert.IsFalse(PerspectiveObjectBinding.IsPerspectiveObject(rootStyle));
        var comment = Line(); var commentDoc = XDocument.Parse(comment.Vector!.Svg);
        string cssComment = "path {stroke: red; /*" + string.Concat(Enumerable.Repeat("/*", 5000)) + " transform: scale(2)";
        commentDoc.Root!.AddFirst(new XElement(commentDoc.Root.Name.Namespace + "style", cssComment));
        comment.Vector = comment.Vector with { Svg = commentDoc.ToString(SaveOptions.DisableFormatting) };
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(comment, Moved(comment), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved));
        Assert.IsNotNull(moved); Assert.IsTrue(moved.Vector!.Svg.Contains(cssComment));
    }

    [TestMethod]
    public void OriginalCurveFamilySurvivesMovesInAnotherGridMode()
    {
        var original = Line();
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original), View, new(CanvasGridMode.TwoPoint), 1, 1, out var straight)); Assert.IsNotNull(straight);
        Assert.AreEqual("6", Path(straight).Attribute("data-perspective-family")!.Value);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(straight, Moved(straight, 2, 3), View, new(CanvasGridMode.FivePoint), 1, 1, out var curved)); Assert.IsNotNull(curved);
        Assert.AreEqual("6", Path(curved).Attribute("data-perspective-family")!.Value); Assert.IsGreaterThan(2, Points(Path(curved)).Length);
    }

    [TestMethod]
    [DataRow(4, PerspectiveLineFamily.CurvedVertical)]
    [DataRow(5, PerspectiveLineFamily.CurvedVertical)]
    [DataRow(5, PerspectiveLineFamily.CurvedHorizontal)]
    public void AFixedCurveFamilyCannotSilentlyBecomeStraightOutsideTheCurvedGridRim(int mode, PerspectiveLineFamily family)
    {
        var settings = new PerspectiveGridSettings((CanvasGridMode)mode, Radius: 20);
        var original = PerspectiveCurveDrawing.Draw(new(110, 110), new(120, 150), View, settings, locked: family).ToAnnotation(AnnotationStyle.Default);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original, 70, 0), View, settings, 1, 1, out var moved));
        Assert.IsNull(moved);
    }

    [TestMethod]
    public void InvalidBoundMovesAreRecognizedButProduceNoReplacement()
    {
        var original = Line();
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original, -500, 0), View, new(CanvasGridMode.FivePoint), 1, 1, out var moved)); Assert.IsNull(moved);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, original.Bounds with { Width = 0 }, View, new(CanvasGridMode.FivePoint), 1, 1, out moved)); Assert.IsNull(moved);
        Assert.IsTrue(PerspectiveObjectBinding.TryReproject(original, Moved(original), View, new(CanvasGridMode.FivePoint), double.NaN, 1, out moved)); Assert.IsNull(moved);
    }
}
