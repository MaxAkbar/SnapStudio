using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CanvasGridTests
{
    private static readonly SizeD Canvas = new(1000, 1000);

    [TestMethod]
    [DataRow(4.0, 4.0)]
    [DataRow(32.0, 32.0)]
    [DataRow(512.0, 512.0)]
    [DataRow(1.0, 4.0)]
    [DataRow(2000.0, 512.0)]
    [DataRow(23.2, 23.0)]
    public void CellSizeIsBoundedAndUsesWholeImagePixels(double input, double expected) =>
        Assert.AreEqual(expected, CanvasGridLayout.NormalizeSpacing(input));

    [TestMethod]
    public void InvalidCellSizeUsesTheDefaultAndInvalidZoomDoesNotRenderLines()
    {
        Assert.AreEqual(32, CanvasGridLayout.NormalizeSpacing(double.NaN));
        Assert.AreEqual(32, CanvasGridLayout.NormalizeSpacing(double.PositiveInfinity));
        Assert.IsEmpty(CanvasGridLayout.Create(32, 0).Lines(0, 100).ToArray());
        Assert.IsEmpty(CanvasGridLayout.Create(32, double.NaN).Lines(0, 100).ToArray());
    }

    [TestMethod]
    [DataRow(0.1)]
    [DataRow(0.5)]
    [DataRow(1.0)]
    [DataRow(2.0)]
    [DataRow(8.0)]
    public void VisibleGridSpacingAndSnapSpacingShareTheSameImageOriginAtEveryZoom(double zoom)
    {
        CanvasGridLayout layout = CanvasGridLayout.Create(4, zoom);
        Assert.IsTrue(layout.DisplaySpacing >= CanvasGridLayout.MinimumDisplaySpacing);
        Assert.AreEqual(layout.SourceSpacing * zoom, layout.DisplaySpacing, 0.000001);
        Assert.AreEqual(0, layout.Lines(0, 100).First().Position);
        Assert.AreEqual(0, layout.SourceSpacing % 4);
        double line = 3 * layout.SourceSpacing;
        AnnotationSnapResult snapped = AnnotationSnapping.Move(new(line + 2 / zoom, 700, 0, 0), [], Canvas, zoom, allowY: false, gridSpacing: layout.SourceSpacing);
        Assert.AreEqual(line, snapped.Bounds.X, 0.000001);
        Assert.IsTrue(snapped.Guides.Single().IsGrid);
    }

    [TestMethod]
    public void DrawingOnlyTheVisibleRegionKeepsLinesOnTheOriginalGrid()
    {
        CanvasGridLine[] lines = CanvasGridLayout.Create(32, 1.5).Lines(70, 210).ToArray();
        CollectionAssert.AreEqual(new double[] { 96, 144, 192 }, lines.Select(line => line.Position).ToArray());
        CollectionAssert.AreEqual(new[] { false, false, true }, lines.Select(line => line.IsMajor).ToArray());
        Assert.IsEmpty(CanvasGridLayout.Create(32, 1).Lines(200, 100).ToArray());
    }

    [TestMethod]
    public void MovingAndResizingSnapToVisibleGridLines()
    {
        AnnotationSnapResult moved = AnnotationSnapping.Move(new(99, 131, 64, 64), [], Canvas, 1, gridSpacing: 32);
        Assert.AreEqual(new RectD(96, 128, 64, 64), moved.Bounds);
        Assert.IsTrue(moved.Guides.All(guide => guide.IsGrid));
        AnnotationSnapResult resized = AnnotationSnapping.Resize(new(96, 128, 93, 64), AnnotationBoundsHandle.Right, [], Canvas, 1, gridSpacing: 32);
        Assert.AreEqual(new RectD(96, 128, 96, 64), resized.Bounds);
        Assert.AreEqual(192, resized.Guides.Single().Position);
    }

    [TestMethod]
    public void GridSnappingCanBeDisabledWithoutDisablingObjectSnapping()
    {
        RectD proposed = new(99, 131, 64, 64);
        Assert.AreEqual(proposed, AnnotationSnapping.Move(proposed, [], Canvas, 1).Bounds);
        AnnotationSnapResult objects = AnnotationSnapping.Move(proposed, [new(102, 300, 80, 40)], Canvas, 1);
        Assert.AreEqual(102, objects.Bounds.X);
        Assert.IsFalse(objects.Guides.Single().IsGrid);
    }

    [TestMethod]
    public void ClosestAlignmentWinsAndObjectMatchesWinEqualDistanceTies()
    {
        RectD proposed = new(99, 200, 64, 64);
        AnnotationSnapResult tie = AnnotationSnapping.Move(proposed, [new(102, 300, 80, 40)], Canvas, 1, allowY: false, gridSpacing: 32);
        Assert.AreEqual(102, tie.Bounds.X);
        Assert.IsFalse(tie.Guides.Single().IsGrid);
        AnnotationSnapResult gridCloser = AnnotationSnapping.Move(proposed, [new(104, 300, 80, 40)], Canvas, 1, allowY: false, gridSpacing: 32);
        Assert.AreEqual(96, gridCloser.Bounds.X);
        Assert.IsTrue(gridCloser.Guides.Single().IsGrid);
    }

    [TestMethod]
    public void GridPreservesSquareConstraintsAndDirectedLineEndpoints()
    {
        AnnotationSnapResult square = AnnotationSnapping.Resize(new(96, 96, 61, 61), AnnotationBoundsHandle.BottomRight, [], Canvas, 1, square: true, gridSpacing: 32);
        Assert.AreEqual(new RectD(96, 96, 64, 64), square.Bounds);
        AnnotationSnapResult line = AnnotationSnapping.Resize(new(200, 200, -101, -69), AnnotationBoundsHandle.End, [], Canvas, 1, gridSpacing: 32);
        Assert.AreEqual(new RectD(200, 200, -104, -72), line.Bounds);
        Assert.IsTrue(line.Guides.All(guide => guide.IsGrid));
    }

    [TestMethod]
    public void GridRespectsLockedAxesMinimumSizesAndCanvasBoundaries()
    {
        AnnotationSnapResult horizontal = AnnotationSnapping.Move(new(99, 131, 64, 64), [], Canvas, 1, allowY: false, gridSpacing: 32);
        Assert.AreEqual(new RectD(96, 131, 64, 64), horizontal.Bounds);
        RectD tooSmall = new(94, 100, 7, 20);
        Assert.AreEqual(tooSmall, AnnotationSnapping.Resize(tooSmall, AnnotationBoundsHandle.Right, [], Canvas, 1, gridSpacing: 32).Bounds);
        AnnotationSnapResult boundary = AnnotationSnapping.Move(new(470, 70, 30, 30), [], new(500, 500), 1, gridSpacing: 32);
        Assert.IsTrue(boundary.Bounds.Right <= 500);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-5.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void InvalidGridSpacingDoesNotCreateGridCandidates(double spacing)
    {
        RectD proposed = new(99, 131, 64, 64);
        AnnotationSnapResult result = AnnotationSnapping.Move(proposed, [], Canvas, 1, gridSpacing: spacing);
        Assert.AreEqual(proposed, result.Bounds);
        Assert.IsEmpty(result.Guides);
    }
}
