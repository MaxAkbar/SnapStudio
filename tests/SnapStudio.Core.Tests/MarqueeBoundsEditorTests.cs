using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class MarqueeBoundsEditorTests
{
    [TestMethod]
    public void DragNormalizesBothDirectionsAndClipsToCanvas()
    {
        Assert.AreEqual(new RectD(10, 20, 80, 60), MarqueeBoundsEditor.Create(new(90, 80), new(10, 20), new(200, 100)));
        Assert.AreEqual(new RectD(90, 80, 110, 20), MarqueeBoundsEditor.Create(new(90, 80), new(500, 400), new(200, 100)));
        Assert.AreEqual(new RectD(0, 0, 90, 80), MarqueeBoundsEditor.Create(new(90, 80), new(-100, -100), new(200, 100)));
    }

    [TestMethod]
    public void SquareKeepsEqualSidesEvenAtCanvasEdges()
    {
        Assert.AreEqual(new RectD(20, 20, 60, 60), MarqueeBoundsEditor.Create(new(20, 20), new(80, 40), new(200, 100), square: true));
        Assert.AreEqual(new RectD(20, 20, 80, 80), MarqueeBoundsEditor.Create(new(20, 20), new(190, 40), new(200, 100), square: true));
        Assert.AreEqual(new RectD(0, 0, 20, 20), MarqueeBoundsEditor.Create(new(20, 20), new(0, 10), new(200, 100), square: true));
    }

    [TestMethod]
    public void CenteredDragExpandsEquallyAndStaysWithinCanvas()
    {
        Assert.AreEqual(new RectD(30, 35, 40, 30), MarqueeBoundsEditor.Create(new(50, 50), new(70, 65), new(200, 100), fromCenter: true));
        Assert.AreEqual(new RectD(0, 0, 100, 100), MarqueeBoundsEditor.Create(new(50, 50), new(190, 90), new(200, 100), square: true, fromCenter: true));
        Assert.AreEqual(new RectD(0, 20, 20, 60), MarqueeBoundsEditor.Create(new(10, 50), new(100, 80), new(200, 100), fromCenter: true));
    }

    [TestMethod]
    public void MovingBoundaryPreservesDimensionsAndStopsAtImageEdges()
    {
        var area = new RectD(20, 30, 60, 40);
        Assert.AreEqual(new RectD(140, 60, 60, 40), MarqueeBoundsEditor.Move(area, 300, 400, new(200, 100)));
        Assert.AreEqual(new RectD(0, 0, 60, 40), MarqueeBoundsEditor.Move(area, -300, -400, new(200, 100)));
        Assert.AreEqual(new RectD(25, 38, 60, 40), MarqueeBoundsEditor.Move(area, 5, 8, new(200, 100)));
    }
}
