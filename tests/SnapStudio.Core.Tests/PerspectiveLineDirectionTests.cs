using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveLineDirectionTests
{
    private static readonly RectD View = new(0, 0, 256, 256);
    private static readonly PointD Start = new(83, 67);
    private static void Collinear(PointD a, PointD b, PointD vanishingPoint) =>
        Assert.AreEqual(0, (b.X - a.X) * (vanishingPoint.Y - a.Y) - (b.Y - a.Y) * (vanishingPoint.X - a.X), 1e-7);

    [TestMethod]
    public void DrawingBetweenVisibleGuidesPreservesTheClickedPointAndConvergesToAVanishingPoint()
    {
        var settings = new PerspectiveGridSettings(CanvasGridMode.TwoPoint, Density: 4);
        Assert.IsNull(PerspectiveGrid.Find(Start, PerspectiveGrid.Create(View, settings), AnnotationSnapping.DisplayTolerance));
        var stroke = PerspectiveLineDrawing.Constrain(Start, new(26, 83), View, settings);
        Assert.AreEqual(Start, stroke.Start); Assert.AreEqual(PerspectiveLineFamily.FirstVanishingPoint, stroke.Direction);
        Collinear(stroke.Start, stroke.End, new(-128, 128));
        Assert.HasCount(1, stroke.Guides);
        Collinear(Start, stroke.Guides[0].Path![0], stroke.Guides[0].Path![1]);
    }

    [TestMethod]
    [DataRow(4)] [DataRow(12)] [DataRow(32)]
    public void GuideDensityNeverChangesTheDirectionOrClickedPosition(int density)
    {
        var expected = PerspectiveLineDrawing.Constrain(Start, new(26, 83), View, new(CanvasGridMode.TwoPoint, Density: 4));
        var actual = PerspectiveLineDrawing.Constrain(Start, new(26, 83), View, new(CanvasGridMode.TwoPoint, Density: density));
        Assert.AreEqual(expected.Start, actual.Start); Assert.AreEqual(expected.End, actual.End); Assert.AreEqual(expected.Direction, actual.Direction);
    }

    [TestMethod]
    [DataRow(.25)] [DataRow(1.0)] [DataRow(4.0)]
    public void AStrokeFollowsPerspectiveWithoutAProximityLimitAtEveryZoom(double zoom)
    {
        var result = PerspectiveLineDrawing.Constrain(Start, new(26, 83), View, new(CanvasGridMode.TwoPoint), zoom, zoom);
        Assert.AreEqual(Start, result.Start); Collinear(Start, result.End, new(-128, 128));
    }

    [TestMethod]
    public void InitialDirectionChoosesTheCorrectAxisAndRemainsLockedDuringTheStroke()
    {
        var options = new PerspectiveGridSettings(CanvasGridMode.TwoPoint);
        var first = PerspectiveLineDrawing.Constrain(Start, new(26, 83), View, options);
        var locked = PerspectiveLineDrawing.Constrain(Start, new(83, 200), View, options, locked: first.Direction);
        var unlocked = PerspectiveLineDrawing.Constrain(Start, new(83, 200), View, options);
        Assert.AreEqual(first.Direction, locked.Direction); Collinear(Start, locked.End, new(-128, 128));
        Assert.AreEqual(PerspectiveLineFamily.Vertical, unlocked.Direction); Assert.AreEqual(Start.X, unlocked.End.X, 1e-8);
        var nearStart = PerspectiveLineDrawing.Constrain(Start, new(84, 68), View, options, locked: first.Direction);
        Assert.AreEqual(first.Direction, nearStart.Direction); Collinear(Start, nearStart.End, new(-128, 128));
    }

    [TestMethod]
    public void BothHorizontalVanishingPointsAndTheThirdPointWorkInBothDrawingDirections()
    {
        var options = new PerspectiveGridSettings(CanvasGridMode.TwoPoint);
        var right = PerspectiveLineDrawing.Constrain(Start, new(180, 85), View, options);
        Assert.AreEqual(PerspectiveLineFamily.SecondVanishingPoint, right.Direction); Collinear(Start, right.End, new(384, 128));
        var outward = PerspectiveLineDrawing.Constrain(Start, new(145, 49), View, options);
        Assert.AreEqual(PerspectiveLineFamily.FirstVanishingPoint, outward.Direction); Collinear(Start, outward.End, new(-128, 128));
        var third = PerspectiveLineDrawing.Constrain(Start, new(95, 10), View, new(CanvasGridMode.ThreePoint));
        Assert.AreEqual(PerspectiveLineFamily.ThirdVanishingPoint, third.Direction); Collinear(Start, third.End, new(128, -256));
    }

    [TestMethod]
    public void OnePointHorizontalAndVerticalLinesCanStartAtAnyPosition()
    {
        var options = new PerspectiveGridSettings(CanvasGridMode.OnePoint);
        var horizontal = PerspectiveLineDrawing.Constrain(Start, new(183, 69), View, options);
        var vertical = PerspectiveLineDrawing.Constrain(Start, new(85, 200), View, options);
        Assert.AreEqual(PerspectiveLineFamily.Horizontal, horizontal.Direction); Assert.AreEqual(67, horizontal.End.Y, 1e-8);
        Assert.AreEqual(PerspectiveLineFamily.Vertical, vertical.Direction); Assert.AreEqual(83, vertical.End.X, 1e-8);
    }

    [TestMethod]
    public void DirectionProjectionClipsAtImageEdgesWithoutMovingTheStart()
    {
        var result = PerspectiveLineDrawing.Constrain(new(10, 10), new(-1000, 200), View, new(CanvasGridMode.TwoPoint), locked: PerspectiveLineFamily.FirstVanishingPoint);
        Assert.AreEqual(new PointD(10, 10), result.Start); Assert.AreEqual(0, result.End.X, 1e-8);
        Assert.IsTrue(result.End.Y >= 0 && result.End.Y <= 256); Collinear(result.Start, result.End, new(-128, 128));
    }

    [TestMethod]
    public void VanishingPointClicksAndTinyGesturesRemainFiniteAndDoNotLockEarly()
    {
        var options = new PerspectiveGridSettings(CanvasGridMode.OnePoint);
        var atPoint = PerspectiveLineDrawing.Constrain(new(128, 128), new(190, 170), View, options);
        Assert.AreEqual(190, atPoint.End.X, 1e-8); Assert.AreEqual(170, atPoint.End.Y, 1e-8);
        var tiny = PerspectiveLineDrawing.Constrain(Start, new(84, 68), View, options);
        Assert.IsNull(tiny.Direction); Assert.IsEmpty(tiny.Guides); Assert.AreEqual(Start, tiny.Start); Assert.AreEqual(new PointD(84, 68), tiny.End);
        Assert.IsNull(PerspectiveLineDrawing.Constrain(Start, new(120, 100), View, options, 0).Direction);
    }

    [TestMethod]
    [DataRow(CanvasGridMode.FourPoint)] [DataRow(CanvasGridMode.FivePoint)]
    public void StraightToolsUseTheLocalCurvedPerspectiveDirectionWithoutJumpingToAGridLine(CanvasGridMode mode)
    {
        PointD start = new(120, 90);
        var result = PerspectiveLineDrawing.Constrain(start, new(130, 200), View, new(mode), locked: PerspectiveLineFamily.CurvedVertical);
        Assert.AreEqual(start, result.Start); Assert.AreEqual(PerspectiveLineFamily.CurvedVertical, result.Direction);
        double r = 256 * .7, x = (start.X - 128) / r, y = (start.Y - 128) / r, a = (x * x + y * y - 1) / (2 * x);
        Assert.AreEqual(0, (result.End.X - start.X) * (x - a) + (result.End.Y - start.Y) * y, 1e-7);
        Assert.HasCount(2, result.Guides.Single().Path!);
    }

    [TestMethod]
    public void NonzeroOriginsAndNonuniformDisplayScalingKeepSourceSpaceConvergence()
    {
        RectD view = new(-80, 40, 400, 300); PointD start = new(40, 115);
        var result = PerspectiveLineDrawing.Constrain(start, new(-30, 130), view, new(CanvasGridMode.TwoPoint), 2, .7);
        Assert.AreEqual(start, result.Start); Assert.AreEqual(PerspectiveLineFamily.FirstVanishingPoint, result.Direction);
        Collinear(start, result.End, new(-280, 190));
    }
}
