using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PolygonalLassoTests
{
    [TestMethod]
    public void ClickedCornersMakeStraightEdgesAndClosingDoesNotAddAnotherVertex()
    {
        var lasso = new PolygonalLasso();
        Assert.IsFalse(lasso.Click(new(2, 2), 1));
        Assert.IsFalse(lasso.Click(new(10, 2), 1));
        Assert.IsFalse(lasso.CanClose);
        Assert.IsFalse(lasso.Click(new(2, 10), 1));
        Assert.IsTrue(lasso.CanClose);
        Assert.IsTrue(lasso.Click(new(2.4, 2.2), 1));
        Assert.HasCount(3, lasso.Vertices);
        PixelSelection mask = PixelSelection.Create(12, 12, PixelSelectionTool.PolygonalLasso, default, lasso.Vertices);
        Assert.IsTrue(mask.Contains(3, 3));
        Assert.IsFalse(mask.Contains(9, 9));
        Assert.AreSequenceEqual(PixelSelection.Create(12, 12, PixelSelectionTool.Lasso, default, lasso.Vertices).Spans, mask.Spans);
    }

    [TestMethod]
    public void DoubleClickDoesNotAddDuplicateCornersAndBackspaceCanCorrectAnOutline()
    {
        var lasso = new PolygonalLasso();
        lasso.Click(new(1, 1), 1);
        lasso.Click(new(9, 1), 1);
        lasso.Click(new(9, 1), 1);
        Assert.HasCount(2, lasso.Vertices);
        lasso.Click(new(7, 8), 1);
        lasso.RemoveLastCorner();
        Assert.IsFalse(lasso.CanClose);
        lasso.Click(new(1, 9), 1);
        Assert.IsTrue(lasso.CanClose);
        Assert.AreEqual(new PointD(1, 9), lasso.Vertices[^1]);
        lasso.Clear();
        Assert.IsEmpty(lasso.Vertices);
        Assert.IsFalse(lasso.CanClose);
    }

    [TestMethod]
    public void ClosingToleranceCannotFinishAnOutlineWithOnlyTwoCorners()
    {
        var lasso = new PolygonalLasso();
        lasso.Click(new(1, 1), 2);
        lasso.Click(new(8, 1), 2);
        Assert.IsFalse(lasso.Click(new(1, 1), 2));
        Assert.HasCount(2, lasso.Vertices);
        Assert.ThrowsExactly<ArgumentException>(() => lasso.Click(new(double.NaN, 0), 2));
    }
}
