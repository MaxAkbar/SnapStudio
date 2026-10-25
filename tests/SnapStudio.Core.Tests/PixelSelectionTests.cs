using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PixelSelectionTests
{
    [TestMethod]
    public void ConcaveAndCrossingLassosUseClosedEvenOddPixelCoverage()
    {
        PixelSelection concave = PixelSelection.Create(12, 12, PixelSelectionTool.Lasso, default,
            [new(1, 1), new(9, 1), new(9, 3), new(4, 3), new(4, 9), new(1, 9)]);
        Assert.AreEqual(34L, concave.PixelCount);
        Assert.IsTrue(concave.Contains(2, 7));
        Assert.IsFalse(concave.Contains(7, 7));
        PixelSelection crossing = PixelSelection.Create(12, 12, PixelSelectionTool.Lasso, default,
            [new(1, 1), new(9, 9), new(1, 9), new(9, 1)]);
        Assert.AreEqual(32L, crossing.PixelCount);
        Assert.IsTrue(crossing.Contains(4, 2));
        Assert.IsTrue(crossing.Contains(4, 7));
        Assert.IsFalse(crossing.Contains(1, 4));
    }
    [TestMethod]
    public void EllipseAndLassoExcludePixelsInsideTheirBoundingBoxButOutsideTheirShape()
    {
        PixelSelection ellipse = PixelSelection.Create(12, 12, PixelSelectionTool.Ellipse, new(2, 2, 8, 8));
        Assert.IsTrue(ellipse.Contains(5, 5));
        Assert.IsFalse(ellipse.Contains(2, 2));
        Assert.AreEqual(new RectD(2, 2, 8, 8), ellipse.Bounds);
        PixelSelection lasso = PixelSelection.Create(12, 12, PixelSelectionTool.Lasso, default,
            [new(1, 1), new(10, 1), new(1, 10)]);
        Assert.IsTrue(lasso.Contains(2, 2));
        Assert.IsFalse(lasso.Contains(8, 8));
        Assert.IsTrue(lasso.PixelCount < lasso.Bounds!.Value.Width * lasso.Bounds.Value.Height);
    }

    [TestMethod]
    public void NarrowRectanglesRetainTheirSourcePixelDimensionsAtTheImageEdge()
    {
        PixelSelection row = PixelSelection.Create(31, 19, PixelSelectionTool.Rectangle, new(0, 18, 31, 1));
        PixelSelection column = PixelSelection.Create(31, 19, PixelSelectionTool.Rectangle, new(30, 0, 1, 19));
        Assert.AreEqual(new RectD(0, 18, 31, 1), row.Bounds);
        Assert.AreEqual(31L, row.PixelCount);
        Assert.AreEqual(new RectD(30, 0, 1, 19), column.Bounds);
        Assert.AreEqual(19L, column.PixelCount);
        Assert.IsFalse(row.Contains(0, 17));
        Assert.IsFalse(column.Contains(29, 0));
    }

    [TestMethod]
    public void SelectionModesMatchPixelBooleanOperationsAndPreserveHoles()
    {
        PixelSelection first = PixelSelection.Create(20, 16, PixelSelectionTool.Ellipse, new(1, 1, 17, 13));
        PixelSelection second = PixelSelection.Create(20, 16, PixelSelectionTool.Rectangle, new(5, 4, 6, 5));
        foreach (PixelSelectionMode mode in Enum.GetValues<PixelSelectionMode>())
        {
            PixelSelection combined = first.Combine(second, mode);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 20; x++)
                {
                    bool a = first.Contains(x, y), b = second.Contains(x, y);
                    bool expected = mode switch
                    {
                        PixelSelectionMode.Replace => b,
                        PixelSelectionMode.Add => a || b,
                        PixelSelectionMode.Subtract => a && !b,
                        _ => a && b
                    };
                    Assert.AreEqual(expected, combined.Contains(x, y), $"{mode} at {x}, {y}");
                }
        }
        PixelSelection hole = first.Combine(second, PixelSelectionMode.Subtract);
        Assert.IsFalse(hole.Contains(7, 6));
        Assert.IsTrue(hole.GetEdges().Any(edge => edge.Start.X == 5 && edge.Start.Y == 4));
        Assert.AreEqual(320L, hole.PixelCount + hole.Invert().PixelCount);
        Assert.AreSequenceEqual(hole.Spans, PixelSelection.FromRectangles(20, 16, hole.GetRectangles()).Spans);
    }

    [TestMethod]
    public void DisjointIslandsHaveNoSelectedPixelsBetweenThemAndMoveWithoutLosingPixels()
    {
        PixelSelection left = PixelSelection.Create(20, 20, PixelSelectionTool.Rectangle, new(1, 1, 2, 2));
        PixelSelection right = PixelSelection.Create(20, 20, PixelSelectionTool.Rectangle, new(10, 8, 2, 2));
        PixelSelection islands = left.Combine(right, PixelSelectionMode.Add);
        PixelSelection moved = islands.Move(100, -100);
        Assert.AreEqual(8L, moved.PixelCount);
        Assert.AreEqual(20d, moved.Bounds!.Value.Right);
        Assert.AreEqual(0d, moved.Bounds.Value.Y);
        Assert.IsFalse(islands.Contains(6, 5));
        Assert.AreSequenceEqual(islands.Spans, islands.Invert().Invert().Spans);
    }

    [TestMethod]
    public void ClippingAndDegenerateLassosCannotSelectPixelsOutsideTheCanvas()
    {
        PixelSelection clipped = PixelSelection.Create(10, 8, PixelSelectionTool.Rectangle, new(-5, -5, 10, 10));
        Assert.AreEqual(new RectD(0, 0, 5, 5), clipped.Bounds);
        Assert.AreEqual(25L, clipped.PixelCount);
        Assert.IsTrue(PixelSelection.Create(10, 8, PixelSelectionTool.Lasso, default, [new(1, 1), new(3, 3)]).IsEmpty);
        Assert.IsTrue(clipped.Combine(clipped, PixelSelectionMode.Subtract).IsEmpty);
        Assert.ThrowsExactly<ArgumentException>(() => clipped.Combine(PixelSelection.Empty(2, 2), PixelSelectionMode.Add));
        Assert.ThrowsExactly<ArgumentException>(() => PixelSelection.Create(10, 8, PixelSelectionTool.Ellipse, new(double.NaN, 0, 5, 5)));
    }

    [TestMethod]
    public void PixelSelectionAndObjectSelectionReplaceEachOtherAndClearDoesNotRetainAMask()
    {
        var document = new CaptureDocument();
        var annotation = new AnnotationObject { LayerId = document.Layers[0].Id };
        document.Annotations.Add(annotation);
        var state = new AnnotationSelectionState();
        state.Select(document, annotation.Id);
        state.SetPixelSelection(PixelSelection.Create(10, 10, PixelSelectionTool.Ellipse, new(1, 1, 7, 7)));
        Assert.IsFalse(state.HasSelection);
        Assert.IsTrue(state.HasMarquee);
        state.RetainExisting(document);
        Assert.IsNotNull(state.PixelMask);
        state.Select(document, annotation.Id);
        Assert.IsNull(state.PixelMask);
        state.SetPixelSelection(PixelSelection.Create(10, 10, PixelSelectionTool.Rectangle, new(0, 3, 10, 1)));
        state.Clear();
        Assert.IsNull(state.PixelMask);
        Assert.IsFalse(state.HasMarquee);
    }
}
