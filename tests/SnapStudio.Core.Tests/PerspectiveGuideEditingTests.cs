using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveGuideEditingTests
{
    private static readonly SizeD Image = new(800, 400);

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void EveryModeExposesItsActualVanishingPoints(int mode)
    {
        var s = new PerspectiveGridSettings((CanvasGridMode)mode);
        var handles = PerspectiveGuideEditing.Handles(Image, s);
        Assert.AreEqual(mode, handles.Count);
        Assert.IsTrue(handles.All(h => double.IsFinite(h.Position.X) && double.IsFinite(h.Position.Y)));
        Assert.IsTrue(handles.All(h => h.Family != GridGuideFamily.Grid));
    }

    [TestMethod]
    public void DraggingAnOffCanvasPointUsesTheOriginalDeltaWithoutJumping()
    {
        var s = new PerspectiveGridSettings(CanvasGridMode.TwoPoint, Left: -50, Right: 150);
        Assert.AreEqual(new PointD(-400, 200), PerspectiveGuideEditing.Handles(Image, s).First(h => h.Family == GridGuideFamily.Left).Position);
        var moved = PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Left, new(80, 24));
        Assert.AreEqual(-40, moved.Left); Assert.AreEqual(s.Right, moved.Right); Assert.AreEqual(s.Horizon, moved.Horizon);
        Assert.AreEqual(s, PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Left, new(0, 0)));
    }

    [TestMethod]
    public void HorizonAndCenterRespectImageAspectRatioAndKeepOtherPositions()
    {
        var s = new PerspectiveGridSettings(CanvasGridMode.OnePoint);
        var horizon = PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Horizon, new(80, 40));
        Assert.AreEqual(60, horizon.Horizon); Assert.AreEqual(50, horizon.CenterX);
        var center = PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Center, new(80, 40));
        Assert.AreEqual(60, center.CenterX); Assert.AreEqual(60, center.Horizon);
    }

    [TestMethod]
    public void ThirdPointMovesIndependentlyOfTheHorizon()
    {
        var s = new PerspectiveGridSettings(CanvasGridMode.ThreePoint);
        var moved = PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Top, new(80, 40));
        Assert.AreEqual(60, moved.CenterX); Assert.AreEqual(-90, moved.Vertical); Assert.AreEqual(50, moved.Horizon);
    }

    [TestMethod]
    [DataRow(PerspectiveGuideHandleKind.Left, 5, -40.0, 0.0)]
    [DataRow(PerspectiveGuideHandleKind.Right, 5, 40.0, 0.0)]
    [DataRow(PerspectiveGuideHandleKind.Top, 4, 0.0, -40.0)]
    [DataRow(PerspectiveGuideHandleKind.Bottom, 4, 0.0, 40.0)]
    public void CurvedPointDraggingPreservesTheSharedRadiusGeometry(PerspectiveGuideHandleKind handle, int mode, double x, double y)
    {
        var s = new PerspectiveGridSettings((CanvasGridMode)mode, Radius: 70);
        var moved = PerspectiveGuideEditing.Move(s, Image, handle, new(x, y));
        Assert.AreEqual(80, moved.Radius); Assert.AreEqual(s.CenterX, moved.CenterX); Assert.AreEqual(s.Horizon, moved.Horizon);
    }

    [TestMethod]
    public void InvalidAndInactiveGesturesDoNotChangeSettings()
    {
        var s = new PerspectiveGridSettings(CanvasGridMode.TwoPoint);
        Assert.AreEqual(s, PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Center, new(50, 50)));
        Assert.AreEqual(s, PerspectiveGuideEditing.Move(s, new(0, 400), PerspectiveGuideHandleKind.Left, new(50, 50)));
        Assert.AreEqual(s, PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Left, new(double.NaN, 50)));
        Assert.IsEmpty(PerspectiveGuideEditing.Handles(new(double.PositiveInfinity, 400), s));
        Assert.AreEqual(-500, PerspectiveGuideEditing.Move(s, Image, PerspectiveGuideHandleKind.Left, new(-100000, 0)).Left);
    }
}
