using System.Text.Json;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveRectangleTests
{
    private static readonly RectD View = new(0, 0, 256, 256);

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
    public void EveryPlaneFollowsItsStraightOrCurvedGuideFamilies(int mode, int plane)
    {
        PointD start = new(45, 45), end = plane == 2 && mode is >= 2 and <= 4 ? new(160, 100) : new(95, 85);
        if (plane == 2 && mode is >= 2 and <= 4) start = new(100, 60);
        var geometry = PerspectiveRectangle.Create(start, end, View, new((CanvasGridMode)mode), (PerspectiveRectanglePlane)plane);
        Assert.IsNotNull(geometry, $"Mode {mode}, plane {plane}");
        Assert.AreEqual(start.X, geometry.Corners[0].X, 1e-7); Assert.AreEqual(start.Y, geometry.Corners[0].Y, 1e-7);
        Assert.HasCount(4, geometry.Corners); Assert.AreEqual(geometry.Outline[0], geometry.Outline[^1]);
        Assert.IsTrue(geometry.Outline.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X >= -1e-6 && p.Y >= -1e-6 && p.X <= 256 + 1e-6 && p.Y <= 256 + 1e-6));
        var (u, v) = mode switch
        {
            1 => plane switch { 0 => ("h", "v"), 2 => ("h", "depth"), _ => ("depth", "v") },
            5 => plane switch { 0 => ("ch", "cv"), 2 => ("ch", "depth"), _ => ("depth", "cv") },
            _ => (plane == 1 ? "right" : "left", plane == 2 ? "right" : mode == 3 ? "third" : mode == 4 ? "cv" : "v")
        };
        int edge = 0;
        foreach (var point in geometry.Outline.Skip(1))
        {
            Assert.AreEqual(0, Equation(edge % 2 == 0 ? u : v, geometry.Corners[edge], point), 1e-7, $"Edge {edge}, mode {mode}, plane {plane}");
            var next = geometry.Corners[(edge + 1) % 4];
            if (Math.Abs(point.X - next.X) + Math.Abs(point.Y - next.Y) < 1e-7) edge++;
        }
        Assert.AreEqual(4, edge);
        if ((mode == 4 && plane != 2) || mode == 5) Assert.IsGreaterThan(5, geometry.Outline.Count);
    }

    private static double Equation(string family, PointD anchor, PointD point)
    {
        double r = 256 * .7, x = (anchor.X - 128) / r, y = (anchor.Y - 128) / r, px = (point.X - 128) / r, py = (point.Y - 128) / r;
        if (family == "cv") return x * (px * px + py * py - 1) + (1 - x * x - y * y) * px;
        if (family == "ch") return y * (px * px + py * py - 1) + (1 - x * x - y * y) * py;
        if (family == "h") return py - y; if (family == "v") return px - x;
        PointD vp = family switch { "left" => new(-128, 128), "right" => new(384, 128), "third" => new(128, -256), _ => new(128, 128) };
        return ((point.X - anchor.X) * (vp.Y - anchor.Y) - (point.Y - anchor.Y) * (vp.X - anchor.X)) / (r * r);
    }

    [TestMethod]
    public void ClickedAndOppositeCornersAreExactBetweenVisibleGuidesAndIndependentOfDensity()
    {
        PointD a = new(83, 67), c = new(181, 110);
        var sparse = PerspectiveRectangle.Create(a, c, View, new(CanvasGridMode.TwoPoint, Density: 4))!;
        var dense = PerspectiveRectangle.Create(a, c, View, new(CanvasGridMode.TwoPoint, Density: 32))!;
        Assert.AreEqual(a.X, sparse.Corners[0].X, 1e-8); Assert.AreEqual(c.X, sparse.Corners[2].X, 1e-8); Assert.AreEqual(c.Y, sparse.Corners[2].Y, 1e-8);
        Assert.AreSequenceEqual(sparse.Outline.ToArray(), dense.Outline.ToArray());
        Assert.AreNotEqual(sparse.Corners[0].Y, sparse.Corners[1].Y);
    }

    [TestMethod]
    [DataRow(45, 45, 95, 85)]
    [DataRow(95, 85, 45, 45)]
    [DataRow(45, 85, 95, 45)]
    [DataRow(95, 45, 45, 85)]
    public void DrawingInEveryQuadrantKeepsTheClickedCorner(int x, int y, int cx, int cy)
    {
        var shape = PerspectiveRectangle.Create(new(x, y), new(cx, cy), View, new(CanvasGridMode.TwoPoint));
        Assert.IsNotNull(shape); Assert.AreEqual(x, shape.Corners[0].X, 1e-7); Assert.AreEqual(y, shape.Corners[0].Y, 1e-7);
    }

    [TestMethod]
    public void CanvasEdgesAndVanishingAxesStopTheFaceWithoutBreakingPerspective()
    {
        var shape = PerspectiveRectangle.Create(new(100, 60), new(255, 255), View, new(CanvasGridMode.ThreePoint));
        Assert.IsNotNull(shape); Assert.IsTrue(shape.Outline.All(p => p.X >= -1e-7 && p.Y >= -1e-7 && p.X <= 256 + 1e-7 && p.Y <= 256 + 1e-7));
        Assert.AreEqual(0, Equation("right", shape.Corners[0], shape.Corners[1]), 1e-7);
        Assert.AreEqual(0, Equation("third", shape.Corners[1], shape.Corners[2]), 1e-7);
    }

    [TestMethod]
    public void NonzeroViewBoxAndRegularFlatRectanglesRemainSupported()
    {
        var shape = PerspectiveRectangle.Create(new(0, 90), new(80, 150), new(-80, 40, 400, 300), new(CanvasGridMode.TwoPoint));
        Assert.IsNotNull(shape); Assert.AreEqual(0, shape.Corners[0].X, 1e-8); Assert.AreEqual(90, shape.Corners[0].Y, 1e-8);
        var flat = PerspectiveRectangle.Flat(new(150, 140), new(80, 60))!;
        Assert.AreEqual(new RectD(80, 60, 70, 80), flat.Bounds); Assert.AreEqual(new PointD(80, 140), flat.Corners[1]);
        Assert.AreEqual(flat.Bounds, PerspectiveRectangle.Create(new(150, 140), new(80, 60), View, new())!.Bounds);
        var ground = PerspectiveRectangle.Create(new(100, 60), new(160, 60), View, new(CanvasGridMode.TwoPoint), PerspectiveRectanglePlane.Ground);
        Assert.IsNotNull(ground); Assert.IsGreaterThan(1, ground.Bounds.Height);
    }

    [TestMethod]
    public void CurvedFacesCannotExtendPastTheGridRimWhenTheDraggedCornerIsOutsideIt()
    {
        foreach (var mode in new[] { CanvasGridMode.FourPoint, CanvasGridMode.FivePoint })
            foreach (var plane in Enum.GetValues<PerspectiveRectanglePlane>())
            {
                if (mode == CanvasGridMode.FourPoint && plane == PerspectiveRectanglePlane.Ground) continue;
                var shape = PerspectiveRectangle.Create(new(110, 110), new(220, 210), View, new(mode, Radius: 20), plane);
                Assert.IsNotNull(shape);
                Assert.IsTrue(shape.Outline.All(p => Math.Pow((p.X - 128) / 51.2, 2) + Math.Pow((p.Y - 128) / 51.2, 2) <= 1 + 1e-7));
            }
    }

    [TestMethod]
    public void DegenerateAndOutOfDomainGesturesDoNotCreateBrokenObjects()
    {
        Assert.IsNull(PerspectiveRectangle.Create(new(-1, 10), new(80, 80), View, new(CanvasGridMode.TwoPoint)));
        Assert.IsNull(PerspectiveRectangle.Create(new(double.NaN, 10), new(80, 80), View, new(CanvasGridMode.TwoPoint)));
        Assert.IsNull(PerspectiveRectangle.Create(new(80, 80), new(80, 80), View, new(CanvasGridMode.TwoPoint)));
        Assert.IsNull(PerspectiveRectangle.Create(new(128, 128), new(180, 180), View, new(CanvasGridMode.OnePoint)));
        Assert.IsNull(PerspectiveRectangle.Create(new(0, 0), new(80, 80), View, new(CanvasGridMode.FivePoint, Radius: 10)));
    }

    [TestMethod]
    public void VectorObjectRetainsItsPaintClosedPathAndGeometryWhenSavedAndRendered()
    {
        var shape = PerspectiveRectangle.Create(new(83, 67), new(181, 110), View, new(CanvasGridMode.TwoPoint))!;
        var style = AnnotationStyle.Default with { Fill = new(255, 0, 0, 255), Stroke = new(0, 0, 255, 255), Pattern = StrokePattern.Dashed, Opacity = .7 };
        var annotation = shape.ToAnnotation(style);
        Assert.AreEqual(AnnotationKind.Vector, annotation.Kind); Assert.IsNotNull(annotation.Vector);
        string normalized = SvgShape.Normalize(annotation.Vector.Svg);
        var path = XDocument.Parse(normalized).Descendants().Single(el => el.Name.LocalName == "path");
        Assert.AreEqual("#FF0000", path.Attribute("fill")!.Value); Assert.AreEqual("8 4", path.Attribute("stroke-dasharray")!.Value); Assert.IsTrue(path.Attribute("d")!.Value.EndsWith(" Z"));
        Assert.AreEqual(4, path.Attribute("d")!.Value.Count(ch => ch is 'M' or 'L'));
        var saved = JsonSerializer.Deserialize<AnnotationObject>(JsonSerializer.Serialize(annotation))!;
        Assert.AreEqual(annotation.Vector, saved.Vector); Assert.AreEqual(annotation.Bounds, saved.Bounds); Assert.AreEqual(style, saved.Style);
        using var bitmap = SvgRasterizer.Render(saved.Vector!.Svg, 200, 150);
        double x = shape.Corners.Average(p => p.X), y = shape.Corners.Average(p => p.Y);
        var pixel = bitmap.GetPixel((int)((x - saved.Bounds.X) / saved.Bounds.Width * 200), (int)((y - saved.Bounds.Y) / saved.Bounds.Height * 150));
        Assert.IsTrue(pixel.R > 240 && pixel.G < 10 && pixel.B < 10 && pixel.A > 240);
    }
}
