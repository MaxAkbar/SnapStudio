using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveToolDrawingTests
{
    private static readonly SizeD Canvas = new(256, 256);
    private static readonly PerspectiveGridStroke Horizontal = new([new(0, 100), new(256, 100)], false);
    private static readonly PerspectiveGridStroke Vertical = new([new(150, 0), new(150, 256)], false);

    [TestMethod]
    public void NewToolObjectsSnapBothDrawCornersAndShowTheirMatchingGuides()
    {
        var drawing = PerspectiveSnapping.Draw(new(37, 102), new(148, 162), AnnotationKind.Rectangle, [Horizontal, Vertical], Canvas, 1);
        Assert.AreEqual(new PointD(37, 100), drawing.Start);
        Assert.AreEqual(new PointD(150, 162), drawing.End);
        Assert.HasCount(2, drawing.Guides);
        Assert.AreSame(Horizontal.Points, drawing.Guides[0].Path);
        Assert.AreSame(Vertical.Points, drawing.Guides[1].Path);
    }

    [TestMethod]
    [DataRow(AnnotationKind.Line)] [DataRow(AnnotationKind.Arrow)]
    public void ReverseLineAndArrowDrawsKeepTheirDirectedEndpoints(AnnotationKind kind)
    {
        var drawing = PerspectiveSnapping.Draw(new(148, 162), new(37, 102), kind, [Horizontal, Vertical], Canvas, 1);
        Assert.AreEqual(new PointD(150, 162), drawing.Start); Assert.AreEqual(new PointD(37, 100), drawing.End);
        Assert.IsTrue(drawing.End.X < drawing.Start.X && drawing.End.Y < drawing.Start.Y);
    }

    [TestMethod]
    [DataRow(AnnotationKind.Rectangle)] [DataRow(AnnotationKind.Ellipse)]
    public void ShiftDrawingKeepsEqualSidesWhenTheEndSnaps(AnnotationKind kind)
    {
        PerspectiveGridStroke grid = new([new(100, 0), new(100, 256)], false);
        var drawing = PerspectiveSnapping.Draw(new(20, 20), new(98, 111), kind, [grid], Canvas, 1, true);
        Assert.AreEqual(new PointD(20, 20), drawing.Start); Assert.AreEqual(new PointD(100, 100), drawing.End);
        Assert.AreEqual(drawing.End.X - drawing.Start.X, drawing.End.Y - drawing.Start.Y, 1e-8);
    }

    [TestMethod]
    public void DisabledOrBypassedGridLeavesPointsFreeButStillHonorsShift()
    {
        var free = PerspectiveSnapping.Draw(new(37, 102), new(148, 162), AnnotationKind.Line, [], Canvas, 1);
        Assert.AreEqual(new PointD(37, 102), free.Start); Assert.AreEqual(new PointD(148, 162), free.End); Assert.IsEmpty(free.Guides);
        var square = PerspectiveSnapping.Draw(new(20, 20), new(98, 111), AnnotationKind.Ellipse, [], Canvas, 1, true);
        Assert.AreEqual(new PointD(98, 98), square.End); Assert.IsEmpty(square.Guides);
    }

    [TestMethod]
    [DataRow(.25)] [DataRow(1.0)] [DataRow(4.0)]
    public void ToolDrawingToleranceStaysTheSameAtDifferentZoomLevels(double zoom)
    {
        var drawing = PerspectiveSnapping.Draw(new(37, 100 + 2 / zoom), new(150 - 2 / zoom, 162), AnnotationKind.Rectangle,
            [Horizontal, Vertical], Canvas, zoom);
        Assert.AreEqual(new PointD(37, 100), drawing.Start); Assert.AreEqual(new PointD(150, 162), drawing.End);
    }

    [TestMethod]
    public void DrawingFollowsTheVisibleCurvedSegmentsRatherThanOnlyTheirBoundingBox()
    {
        PerspectiveGridStroke curve = new([new(0, 100), new(50, 60), new(100, 100)], false);
        var drawing = PerspectiveSnapping.Draw(new(25.8, 81), new(74.2, 81), AnnotationKind.Vector, [curve], Canvas, 1);
        Assert.AreEqual(25, drawing.Start.X, 1e-7); Assert.AreEqual(80, drawing.Start.Y, 1e-7);
        Assert.AreEqual(75, drawing.End.X, 1e-7); Assert.AreEqual(80, drawing.End.Y, 1e-7);
        Assert.HasCount(1, drawing.Guides); Assert.AreSame(curve.Points, drawing.Guides[0].Path);
    }

    [TestMethod]
    [DataRow(AnnotationKind.Line)] [DataRow(AnnotationKind.Arrow)]
    public void LinesPreferOneStraightGuideWhenTheStartIsNearAnotherGuideCrossing(AnnotationKind kind)
    {
        PerspectiveGridStroke diagonal = new([new(0, 0), new(256, 256)], false);
        PerspectiveGridStroke crossing = new([new(50, 0), new(50, 256)], false);
        var drawing = PerspectiveSnapping.Draw(new(50.2, 52), new(101, 103), kind, [crossing, diagonal], Canvas, 1);
        Assert.AreEqual(drawing.Start.X, drawing.Start.Y, 1e-8);
        Assert.AreEqual(drawing.End.X, drawing.End.Y, 1e-8);
        Assert.HasCount(1, drawing.Guides); Assert.AreSame(diagonal.Points, drawing.Guides[0].Path);
    }

    [TestMethod]
    public void ClickOnlyDrawingStaysZeroLengthAndCannotSnapOutsideTheImage()
    {
        var click = PerspectiveSnapping.Draw(new(148, 162), new(148, 162), AnnotationKind.Arrow, [Vertical], Canvas, 1);
        Assert.AreEqual(click.Start, click.End);
        PerspectiveGridStroke outside = new([new(259, 0), new(259, 256)], false);
        var edge = PerspectiveSnapping.Draw(new(60, 60), new(255, 90), AnnotationKind.Rectangle, [outside], Canvas, 1);
        Assert.AreEqual(new PointD(255, 90), edge.End); Assert.IsEmpty(edge.Guides);
        var invalid = PerspectiveSnapping.Draw(new(double.NaN, 1), new(10, 10), AnnotationKind.Rectangle, [Vertical], Canvas, 1, true);
        Assert.IsEmpty(invalid.Guides);
    }
}
