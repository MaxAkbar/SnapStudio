using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveGridTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void EveryPerspectiveModeProducesFiniteClippedGuidesAtNonzeroOrigins(int mode)
    {
        var view = new RectD(-70, 40, 400, 300);
        var grid = PerspectiveGrid.Create(view, new((CanvasGridMode)mode));
        Assert.IsNotEmpty(grid);
        Assert.IsTrue(grid.All(stroke => stroke.Points.Count > 1));
        foreach (PointD p in grid.SelectMany(stroke => stroke.Points))
        {
            Assert.IsTrue(double.IsFinite(p.X) && double.IsFinite(p.Y));
            Assert.IsTrue(p.X >= view.X - 1e-7 && p.X <= view.Right + 1e-7 && p.Y >= view.Y - 1e-7 && p.Y <= view.Bottom + 1e-7);
        }
        Assert.AreEqual(mode <= 3, grid.All(stroke => stroke.Points.Count == 2));
    }

    [TestMethod]
    public void FourPointCombinesTwoStraightFansWithCurvedVerticalsAndFivePointCurvesBothAxes()
    {
        var view = new RectD(0, 0, 400, 400);
        var four = PerspectiveGrid.Create(view, new(CanvasGridMode.FourPoint, Radius: 40));
        Assert.IsTrue(four.Any(s => s.Points.Count == 2) && four.Any(s => s.Points.Count > 2));
        var five = PerspectiveGrid.Create(view, new(CanvasGridMode.FivePoint, Radius: 40));
        Assert.IsTrue(five.Count(s => s.Points.Count > 2) > four.Count(s => s.Points.Count > 2));
        Assert.IsTrue(five.Any(s => s.Points.Count == 2)); // Depth guides converge at the fifth, central point.
    }

    [TestMethod]
    public void SettingsAndDegenerateInputsStayBounded()
    {
        var s = new PerspectiveGridSettings((CanvasGridMode)99, double.NaN, double.PositiveInfinity, -1000, 1000, double.NaN, 0, 1000).Normalize();
        Assert.AreEqual(CanvasGridMode.Rectangular, s.Mode); Assert.AreEqual(50, s.CenterX); Assert.AreEqual(50, s.Horizon);
        Assert.AreEqual(-500, s.Left); Assert.AreEqual(500, s.Right); Assert.AreEqual(-100, s.Vertical);
        Assert.AreEqual(10, s.Radius); Assert.AreEqual(32, s.Density);
        Assert.IsEmpty(PerspectiveGrid.Create(new(0, 0, 0, 100), new(CanvasGridMode.OnePoint)));
        Assert.IsEmpty(PerspectiveGrid.Create(new(0, 0, double.NaN, 100), new(CanvasGridMode.OnePoint)));
        Assert.IsEmpty(PerspectiveGrid.Create(new(0, 0, 100, 100), new()));
        var grid = PerspectiveGrid.Create(new(0, 0, 4096, 16384), new(CanvasGridMode.FivePoint, Density: 32));
        Assert.IsTrue(grid.Sum(s => s.Points.Count) < 20000);
        Assert.IsNull(PerspectiveGrid.Find(new(0, 0), grid, 6, scaleX: 0));
    }

    [TestMethod]
    [DataRow(0.25)]
    [DataRow(1.0)]
    [DataRow(4.0)]
    public void AngledGuidesUseCoupledCoordinatesAndScreenSpaceTolerance(double zoom)
    {
        PerspectiveGridStroke stroke = new([new(0, 0), new(100, 100)], false);
        var result = PerspectiveGrid.Find(new(20 + 2 / zoom, 20), [stroke], 6, scaleX: zoom, scaleY: zoom);
        Assert.IsNotNull(result); Assert.AreEqual(result.Point.X, result.Point.Y, 1e-8);
        Assert.AreEqual(Math.Sqrt(2), result.Distance, 1e-8);
        Assert.IsNull(PerspectiveGrid.Find(new(20 + 12 / zoom, 20), [stroke], 6, scaleX: zoom, scaleY: zoom));
    }

    [TestMethod]
    public void DirectionConstraintsIntersectGuidesWithoutChangingTheLockedCoordinate()
    {
        PerspectiveGridStroke stroke = new([new(0, 0), new(100, 100)], false);
        Assert.AreEqual(new PointD(21, 21), PerspectiveGrid.Find(new(19, 21), [stroke], 6, new(1, 0))!.Point);
        Assert.AreEqual(new PointD(19, 19), PerspectiveGrid.Find(new(19, 21), [stroke], 6, new(0, 1))!.Point);
        Assert.IsNull(PerspectiveGrid.Find(new(19, 21), [stroke], 6, new(1, 1)));
    }

    [TestMethod]
    public void GroupMovementPreservesSizeAndBoundsAndReportsTheActualGuidePath()
    {
        PerspectiveGridStroke stroke = new([new(10, 0), new(110, 100)], false);
        RectD before = new(22, 23, 20, 20);
        var result = PerspectiveSnapping.Move(before, [stroke], new(256, 256), 1);
        Assert.AreEqual(20, result.Bounds.Width); Assert.AreEqual(20, result.Bounds.Height);
        Assert.AreEqual(-(result.Bounds.Y - before.Y), result.Bounds.X - before.X, 1e-7);
        Assert.AreSame(stroke.Points, result.Guides.Single().Path);
        var locked = PerspectiveSnapping.Move(before, [stroke], new(256, 256), 1, allowY: false);
        Assert.AreEqual(before.Y, locked.Bounds.Y);
        var edge = PerspectiveSnapping.Move(new(0, 0, 256, 256), [stroke], new(256, 256), 1);
        Assert.AreEqual(new RectD(0, 0, 256, 256), edge.Bounds);
    }

    [TestMethod]
    public void ResizingPreservesSquaresMinimumSizesAndDirectedEndpoints()
    {
        PerspectiveGridStroke diagonal = new([new(0, 120), new(120, 0)], false);
        var square = PerspectiveSnapping.Resize(new(20, 20, 39, 39), AnnotationBoundsHandle.BottomRight, [diagonal], new(256, 256), 1, true);
        Assert.AreEqual(40, square.Bounds.Width, 1e-8); Assert.AreEqual(square.Bounds.Width, square.Bounds.Height, 1e-8);
        PerspectiveGridStroke vertical = new([new(40, 0), new(40, 256)], false);
        var line = PerspectiveSnapping.Resize(new(100, 100, -62, -68), AnnotationBoundsHandle.End, [vertical], new(256, 256), 1, true);
        Assert.AreEqual(40, line.Bounds.Right, 1e-8);
        Assert.AreEqual(-62.0 / 68, line.Bounds.Width / -line.Bounds.Height, 1e-8);
        RectD small = new(38, 20, 7, 20);
        Assert.AreEqual(small, PerspectiveSnapping.Resize(small, AnnotationBoundsHandle.Right, [vertical], new(256, 256), 1).Bounds);
    }

    [TestMethod]
    public void MovingTheHorizonAndVanishingPointsChangesGuidesAndDensityChangesTheirCount()
    {
        var view = new RectD(0, 0, 400, 300);
        var normal = PerspectiveGrid.Create(view, new(CanvasGridMode.ThreePoint));
        var shifted = PerspectiveGrid.Create(view, new(CanvasGridMode.ThreePoint, CenterX: 30, Horizon: 25, Left: -100, Vertical: 200));
        Assert.IsFalse(normal.SelectMany(s => s.Points).SequenceEqual(shifted.SelectMany(s => s.Points)));
        Assert.IsTrue(PerspectiveGrid.Create(view, new(CanvasGridMode.FivePoint, Density: 4)).Count
            < PerspectiveGrid.Create(view, new(CanvasGridMode.FivePoint, Density: 24)).Count);
    }
}
