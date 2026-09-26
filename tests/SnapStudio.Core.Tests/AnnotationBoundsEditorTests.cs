using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class AnnotationBoundsEditorTests
{
    [TestMethod]
    public void Move_ClampsBoundsInsideSource()
    {
        RectD moved = AnnotationBoundsEditor.Move(
            new RectD(80, 90, 40, 30),
            50,
            50,
            new SizeD(120, 110));

        Assert.AreEqual(new RectD(80, 80, 40, 30), moved);
    }

    [TestMethod]
    public void Move_PreservesDirectedBounds()
    {
        RectD moved = AnnotationBoundsEditor.Move(
            new RectD(80, 90, -40, -30),
            -20,
            -20,
            new SizeD(120, 110));

        Assert.AreEqual(new RectD(60, 70, -40, -30), moved);
    }

    [TestMethod]
    public void Resize_WithBottomRightHandle_ExpandsToDragPoint()
    {
        RectD resized = AnnotationBoundsEditor.Resize(
            new RectD(20, 30, 40, 50),
            AnnotationBoundsHandle.BottomRight,
            new PointD(100, 120),
            new SizeD(200, 200));

        Assert.AreEqual(new RectD(20, 30, 80, 90), resized);
    }

    [TestMethod]
    public void Resize_WithTopLeftHandle_ClampsToMinimumSize()
    {
        RectD resized = AnnotationBoundsEditor.Resize(
            new RectD(20, 30, 40, 50),
            AnnotationBoundsHandle.TopLeft,
            new PointD(100, 120),
            new SizeD(200, 200),
            minimumSize: 10);

        Assert.AreEqual(new RectD(50, 70, 10, 10), resized);
    }

    [TestMethod]
    public void Resize_WithLeftHandle_ClampsToSource()
    {
        RectD resized = AnnotationBoundsEditor.Resize(
            new RectD(20, 30, 40, 50),
            AnnotationBoundsHandle.Left,
            new PointD(-100, 120),
            new SizeD(200, 200));

        Assert.AreEqual(new RectD(0, 30, 60, 50), resized);
    }

    [TestMethod]
    public void MoveEndpoint_WithStartHandle_PreservesEndPoint()
    {
        RectD edited = AnnotationBoundsEditor.MoveEndpoint(
            new RectD(20, 30, 40, 50),
            AnnotationBoundsHandle.Start,
            new PointD(10, 15),
            new SizeD(200, 200));

        Assert.AreEqual(new RectD(10, 15, 50, 65), edited);
    }

    [TestMethod]
    public void MoveEndpoint_WithEndHandle_PreservesStartPoint()
    {
        RectD edited = AnnotationBoundsEditor.MoveEndpoint(
            new RectD(20, 30, 40, 50),
            AnnotationBoundsHandle.End,
            new PointD(100, 120),
            new SizeD(200, 200));

        Assert.AreEqual(new RectD(20, 30, 80, 90), edited);
    }
}
