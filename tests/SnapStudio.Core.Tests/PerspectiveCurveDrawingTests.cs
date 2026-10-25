using System.Text.Json;
using System.Xml.Linq;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveCurveDrawingTests
{
    private static readonly RectD View = new(0, 0, 256, 256);
    private static readonly PointD Start = new(83, 67);

    private static PointD OnCurve(PointD start, double phi, bool horizontal, RectD view, PerspectiveGridSettings settings)
    {
        double cx = view.X + view.Width * settings.CenterX / 100, cy = view.Y + view.Height * settings.Horizon / 100;
        double r = Math.Min(view.Width, view.Height) * settings.Radius / 100;
        double u = (start.X - cx) / r, v = (start.Y - cy) / r;
        if (horizontal) (u, v) = (v, u);
        double a = Math.Abs((u * u + v * v - 1) / (2 * u)), cosine = Math.Cos(phi), c2 = cosine * cosine;
        double x = Math.Sign(u) * c2 / (Math.Sqrt(a * a + c2) + a), y = Math.Sin(phi);
        return horizontal ? new(cx + y * r, cy + x * r) : new(cx + x * r, cy + y * r);
    }

    private static void OnSameCircle(PointD start, IEnumerable<PointD> points, bool horizontal, RectD view, PerspectiveGridSettings settings)
    {
        double cx = view.X + view.Width * settings.CenterX / 100, cy = view.Y + view.Height * settings.Horizon / 100;
        double r = Math.Min(view.Width, view.Height) * settings.Radius / 100;
        double u = (start.X - cx) / r, v = (start.Y - cy) / r;
        if (horizontal) (u, v) = (v, u);
        foreach (var p in points)
        {
            double x = (p.X - cx) / r, y = (p.Y - cy) / r;
            if (horizontal) (x, y) = (y, x);
            Assert.AreEqual(0, u * (x * x + y * y - 1) + (1 - u * u - v * v) * x, 1e-8);
        }
    }

    [TestMethod]
    [DataRow(CanvasGridMode.FourPoint)] [DataRow(CanvasGridMode.FivePoint)]
    public void AVerticalPerspectiveStrokeActuallyBendsAlongTheCircleThroughItsClickedStart(CanvasGridMode mode)
    {
        var settings = new PerspectiveGridSettings(mode);
        PointD end = OnCurve(Start, .55, false, View, settings);
        var result = PerspectiveCurveDrawing.Draw(Start, end, View, settings, locked: PerspectiveLineFamily.CurvedVertical);
        Assert.AreEqual(Start, result.Points[0]); Assert.IsGreaterThan(2, result.Points.Count);
        Assert.AreEqual(end.X, result.Points[^1].X, 1e-5); Assert.AreEqual(end.Y, result.Points[^1].Y, 1e-5);
        OnSameCircle(Start, result.Points, false, View, settings);
        Assert.IsTrue(result.Points.Skip(1).SkipLast(1).Any(p => Math.Abs((p.X - Start.X) * (end.Y - Start.Y) - (p.Y - Start.Y) * (end.X - Start.X)) > 1));
        Assert.HasCount(1, result.Guides); OnSameCircle(Start, result.Guides[0].Path!, false, View, settings);
    }

    [TestMethod]
    public void FivePointHorizontalFamilyBendsAcrossTheImageAndRetainsItsClickedStart()
    {
        var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint);
        PointD end = OnCurve(Start, .6, true, View, settings);
        var result = PerspectiveCurveDrawing.Draw(Start, end, View, settings, locked: PerspectiveLineFamily.CurvedHorizontal);
        Assert.AreEqual(PerspectiveLineFamily.CurvedHorizontal, result.Direction); Assert.AreEqual(Start, result.Points[0]);
        Assert.AreEqual(end.X, result.Points[^1].X, 1e-5); Assert.AreEqual(end.Y, result.Points[^1].Y, 1e-5);
        OnSameCircle(Start, result.Points, true, View, settings); Assert.IsGreaterThan(2, result.Points.Count);
    }

    [TestMethod]
    [DataRow(4)] [DataRow(12)] [DataRow(32)]
    public void CurvesBetweenDisplayedGuidesAreIndependentOfGuideDensity(int density)
    {
        var expected = PerspectiveCurveDrawing.Draw(Start, new(110, 210), View, new(CanvasGridMode.FourPoint, Density: 4), locked: PerspectiveLineFamily.CurvedVertical);
        var actual = PerspectiveCurveDrawing.Draw(Start, new(110, 210), View, new(CanvasGridMode.FourPoint, Density: density), locked: PerspectiveLineFamily.CurvedVertical);
        Assert.AreSequenceEqual(expected.Points.ToArray(), actual.Points.ToArray());
        Assert.AreEqual(Start, actual.Points[0]);
    }

    [TestMethod]
    [DataRow(.25)] [DataRow(1.0)] [DataRow(4.0)]
    public void ZoomChangesNeitherTheClickedPointNorTheCircularFamily(double zoom)
    {
        var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint);
        var result = PerspectiveCurveDrawing.Draw(Start, new(100, 220), View, settings, zoom, zoom, PerspectiveLineFamily.CurvedVertical);
        Assert.AreEqual(Start, result.Points[0]); OnSameCircle(Start, result.Points, false, View, settings);
    }

    [TestMethod]
    public void InitialDirectionLocksTheCurvedFamilyAndReverseDrawingFollowsTheSameCircle()
    {
        var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint);
        var first = PerspectiveCurveDrawing.Draw(Start, new(83, 95), View, settings);
        Assert.AreEqual(PerspectiveLineFamily.CurvedVertical, first.Direction);
        var locked = PerspectiveCurveDrawing.Draw(Start, new(220, 130), View, settings, locked: first.Direction);
        Assert.AreEqual(first.Direction, locked.Direction); OnSameCircle(Start, locked.Points, false, View, settings);
        PointD end = OnCurve(Start, .4, false, View, settings);
        var reverse = PerspectiveCurveDrawing.Draw(end, Start, View, settings, locked: first.Direction);
        Assert.AreEqual(end, reverse.Points[0]); Assert.AreEqual(Start.X, reverse.Points[^1].X, 1e-5); Assert.AreEqual(Start.Y, reverse.Points[^1].Y, 1e-5);
        OnSameCircle(Start, reverse.Points, false, View, settings);
    }

    [TestMethod]
    public void CenterGuidesAndPoleClicksStayFiniteWithoutInventingAnUnspecifiedCircle()
    {
        var center = PerspectiveCurveDrawing.Draw(new(128, 60), new(140, 200), View, new(CanvasGridMode.FivePoint), locked: PerspectiveLineFamily.CurvedVertical);
        Assert.HasCount(2, center.Points); Assert.AreEqual(128, center.Points[^1].X, 1e-7);
        var pole = PerspectiveCurveDrawing.Draw(new(128, 51.2), new(150, 130), View, new(CanvasGridMode.FivePoint, Radius: 30), locked: PerspectiveLineFamily.CurvedVertical);
        Assert.IsTrue(pole.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)));
        var nearAxis = PerspectiveCurveDrawing.Draw(new(128.00000001, 60), new(140, 200), View, new(CanvasGridMode.FivePoint), locked: PerspectiveLineFamily.CurvedVertical);
        Assert.IsTrue(nearAxis.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)));
    }

    [TestMethod]
    public void ACurveStopsAtItsFirstCanvasExitRatherThanJumpingToAReenteringSegment()
    {
        PointD start = new(250, 20);
        var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint);
        var result = PerspectiveCurveDrawing.Draw(start, new(250, 240), View, settings, locked: PerspectiveLineFamily.CurvedVertical);
        Assert.AreEqual(start, result.Points[0]); Assert.AreEqual(256, result.Points[^1].X, 1e-5);
        Assert.IsTrue(result.Points[^1].Y < 128); Assert.IsTrue(result.Points.All(p => p.X >= -1e-7 && p.X <= 256 + 1e-7 && p.Y >= -1e-7 && p.Y <= 256 + 1e-7));
        OnSameCircle(start, result.Points, false, View, settings);
        Assert.IsTrue(result.Guides[0].Path!.All(p => p.Y < 128));
    }

    [TestMethod]
    public void CurvedGuidesStopAtTheirRimWhenThePointerContinuesOutsideIt()
    {
        PointD start = new(110, 110); var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint, Radius: 20);
        var result = PerspectiveCurveDrawing.Draw(start, new(220, 210), View, settings, locked: PerspectiveLineFamily.CurvedVertical);
        Assert.IsTrue(result.Points.All(p => Math.Pow((p.X - 128) / 51.2, 2) + Math.Pow((p.Y - 128) / 51.2, 2) <= 1 + 1e-8));
        OnSameCircle(start, result.Points, false, View, settings);
    }

    [TestMethod]
    public void NonzeroViewBoxesAndUnequalDisplayScalesUseTheClosestVisibleScreenPoint()
    {
        RectD view = new(-80, 40, 400, 300); PointD start = new(0, 90), end = new(85, 290);
        var settings = new PerspectiveGridSettings(CanvasGridMode.FivePoint);
        var result = PerspectiveCurveDrawing.Draw(start, end, view, settings, 2.2, .6, PerspectiveLineFamily.CurvedVertical);
        Assert.AreEqual(start, result.Points[0]); OnSameCircle(start, result.Points, false, view, settings);
        double Score(PointD p) => Math.Pow((p.X - end.X) * 2.2, 2) + Math.Pow((p.Y - end.Y) * .6, 2);
        double bestDense = Enumerable.Range(0, 20001).Select(i => OnCurve(start, -Math.PI / 2 + Math.PI * i / 20000, false, view, settings))
            .Where(p => p.X >= view.X && p.X <= view.Right && p.Y >= view.Y && p.Y <= view.Bottom).Min(Score);
        Assert.IsTrue(Score(result.Points[^1]) <= bestDense + .0001);
    }

    [TestMethod]
    [DataRow(CanvasGridMode.OnePoint)] [DataRow(CanvasGridMode.TwoPoint)] [DataRow(CanvasGridMode.ThreePoint)]
    public void StraightPerspectiveModesKeepTheExistingRulerGeometry(CanvasGridMode mode)
    {
        var settings = new PerspectiveGridSettings(mode); PointD end = new(180, 90);
        var ruler = PerspectiveLineDrawing.Constrain(Start, end, View, settings);
        var result = PerspectiveCurveDrawing.Draw(Start, end, View, settings);
        Assert.HasCount(2, result.Points); Assert.AreEqual(ruler.Start, result.Points[0]); Assert.AreEqual(ruler.End, result.Points[^1]); Assert.AreEqual(ruler.Direction, result.Direction);
    }

    [TestMethod]
    public void TinyAndRectangularGesturesRemainFreeUntilAValidDirectionIsChosen()
    {
        var tiny = PerspectiveCurveDrawing.Draw(Start, new(84, 68), View, new(CanvasGridMode.FivePoint));
        Assert.IsNull(tiny.Direction); Assert.IsEmpty(tiny.Guides); Assert.AreSequenceEqual(new[] { Start, new PointD(84, 68) }, tiny.Points.ToArray());
        var flat = PerspectiveCurveDrawing.Draw(Start, new(180, 120), View, new());
        Assert.IsNull(flat.Direction); Assert.AreEqual(new PointD(180, 120), flat.Points[^1]);
        var locked = PerspectiveCurveDrawing.Draw(Start, new(84, 68), View, new(CanvasGridMode.FivePoint), locked: PerspectiveLineFamily.CurvedVertical);
        Assert.AreEqual(PerspectiveLineFamily.CurvedVertical, locked.Direction); OnSameCircle(Start, locked.Points, false, View, new(CanvasGridMode.FivePoint));
    }

    [TestMethod]
    public void InvalidGeometryReturnsNoDrawablePath()
    {
        Assert.IsEmpty(PerspectiveCurveDrawing.Draw(new(double.NaN, 60), new(80, 120), View, new(CanvasGridMode.FivePoint)).Points);
        Assert.IsEmpty(PerspectiveCurveDrawing.Draw(Start, new(80, double.PositiveInfinity), View, new(CanvasGridMode.FivePoint)).Points);
        Assert.IsEmpty(PerspectiveCurveDrawing.Draw(new(-10, 60), new(80, 120), View, new(CanvasGridMode.FivePoint)).Points);
        Assert.IsEmpty(PerspectiveCurveDrawing.Draw(Start, new(80, 120), View, new(CanvasGridMode.FivePoint), 0).Points);
        Assert.IsEmpty(PerspectiveCurveDrawing.Draw(Start, new(80, 120), new(0, 0, 0, 256), new(CanvasGridMode.FivePoint)).Points);
    }

    [TestMethod]
    public void SavedLineIsOneOpenEditableVectorWithTheSelectedStrokeAndNoFill()
    {
        var result = PerspectiveCurveDrawing.Draw(Start, new(100, 220), View, new(CanvasGridMode.FivePoint), locked: PerspectiveLineFamily.CurvedVertical);
        var style = AnnotationStyle.Default with { Stroke = new(0, 0, 255, 255), Fill = new(255, 0, 0, 255), StrokeThickness = 4, Pattern = StrokePattern.DashDot, Opacity = .6 };
        var annotation = result.ToAnnotation(style); Assert.AreEqual(AnnotationKind.Vector, annotation.Kind); Assert.AreEqual("Perspective line", annotation.Vector!.Name);
        var path = XDocument.Parse(SvgShape.Normalize(annotation.Vector.Svg)).Descendants().Single(e => e.Name.LocalName == "path");
        Assert.AreEqual("none", path.Attribute("fill")!.Value); Assert.AreEqual("#0000FF", path.Attribute("stroke")!.Value); Assert.AreEqual("16 8 4 8", path.Attribute("stroke-dasharray")!.Value);
        Assert.IsFalse(path.Attribute("d")!.Value.EndsWith('Z')); Assert.IsGreaterThan(2, path.Attribute("d")!.Value.Count(ch => ch is 'M' or 'L'));
        var saved = JsonSerializer.Deserialize<AnnotationObject>(JsonSerializer.Serialize(annotation))!;
        Assert.AreEqual(annotation.Vector, saved.Vector); Assert.AreEqual(annotation.Bounds, saved.Bounds); Assert.AreEqual(style, saved.Style);
        using var bitmap = SvgRasterizer.Render(saved.Vector!.Svg, 512, 512);
        var p = result.Points[result.Points.Count / 2];
        var pixel = bitmap.GetPixel((int)((p.X - saved.Bounds.X) / saved.Bounds.Width * 512), (int)((p.Y - saved.Bounds.Y) / saved.Bounds.Height * 512));
        Assert.IsTrue(pixel.B > 200 && pixel.R < 20 && pixel.G < 20 && pixel.A > 0);
    }

    [TestMethod]
    public void InvalidPathsCannotBeSerializedIntoAnSvgObject()
    {
        Assert.Throws<InvalidOperationException>(() => new PerspectiveCurveDrawingResult([], []).ToAnnotation(AnnotationStyle.Default));
        Assert.Throws<InvalidOperationException>(() => new PerspectiveCurveDrawingResult([Start, new(double.NaN, 0)], []).ToAnnotation(AnnotationStyle.Default));
    }
}
