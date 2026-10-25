using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class AnnotationSnappingTests
{
    private static readonly SizeD Canvas = new(1000, 1000);

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    public void MoveAlignsLeftCenterAndRightToOtherObjectAnchors(int movingAnchor, int targetAnchor)
    {
        double target = 100 + 40 * targetAnchor, expectedX = target - 10 * movingAnchor;
        AnnotationSnapResult result = AnnotationSnapping.Move(new(expectedX + 3, 60, 20, 16), [new(100, 200, 80, 60)], Canvas, 1);
        Assert.AreEqual(new RectD(expectedX, 60, 20, 16), result.Bounds);
        AnnotationSnapGuide guide = result.Guides.Single();
        Assert.AreEqual(SnapGuideAxis.Vertical, guide.Axis);
        Assert.AreEqual(target, guide.Position);
        Assert.AreEqual(movingAnchor == 1 && targetAnchor == 1, guide.IsCenter);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    public void MoveAlignsTopMiddleAndBottomToOtherObjectAnchors(int movingAnchor, int targetAnchor)
    {
        double target = 100 + 40 * targetAnchor, expectedY = target - 10 * movingAnchor;
        AnnotationSnapResult result = AnnotationSnapping.Move(new(60, expectedY + 3, 16, 20), [new(200, 100, 60, 80)], Canvas, 1);
        Assert.AreEqual(new RectD(60, expectedY, 16, 20), result.Bounds);
        Assert.AreEqual(SnapGuideAxis.Horizontal, result.Guides.Single().Axis);
        Assert.AreEqual(target, result.Guides.Single().Position);
    }

    [TestMethod]
    public void MoveCanMatchBothCentersAndGuidesSpanBothObjects()
    {
        AnnotationSnapResult result = AnnotationSnapping.Move(new(128, 117, 30, 30), [new(100, 100, 80, 60)], Canvas, 1);
        Assert.AreEqual(new RectD(125, 115, 30, 30), result.Bounds);
        Assert.HasCount(2, result.Guides);
        Assert.IsTrue(result.Guides.All(guide => guide.IsCenter));
        Assert.AreEqual(new AnnotationSnapGuide(SnapGuideAxis.Vertical, 140, 100, 160, true), result.Guides[0]);
    }

    [TestMethod]
    [DataRow(0.1)]
    [DataRow(0.5)]
    [DataRow(1.0)]
    [DataRow(4.0)]
    [DataRow(8.0)]
    public void ToleranceStaysSixDisplayPixelsAtEveryZoom(double zoom)
    {
        RectD inside = new(100 + 5 / zoom, 200, 0, 30), outside = inside with { X = 100 + 7 / zoom };
        Assert.AreEqual(100, AnnotationSnapping.Move(inside, [new(100, 700, 0, 0)], Canvas, zoom).Bounds.X, 0.000001);
        Assert.AreEqual(outside, AnnotationSnapping.Move(outside, [new(100, 700, 0, 0)], Canvas, zoom).Bounds);
    }

    [TestMethod]
    public void CanvasEdgesAndCentersAreAvailableWithoutOtherObjects()
    {
        AnnotationSnapResult center = AnnotationSnapping.Move(new(488, 482, 20, 40), [], Canvas, 1);
        Assert.AreEqual(new RectD(490, 480, 20, 40), center.Bounds);
        Assert.HasCount(2, center.Guides);
        Assert.IsTrue(center.Guides.All(guide => guide.From == 0 && guide.To == 1000));
        Assert.AreEqual(0, AnnotationSnapping.Move(new(3, 75, 20, 20), [], Canvas, 1).Bounds.X);
        Assert.AreEqual(980, AnnotationSnapping.Move(new(977, 75, 20, 20), [], Canvas, 1).Bounds.X);
    }

    [TestMethod]
    public void ClosestMatchWinsAndTargetOrderingDoesNotChangeThePosition()
    {
        RectD moving = new(103, 60, 20, 20), farther = new(100, 200, 80, 60), nearer = new(104, 300, 80, 60);
        Assert.AreEqual(104, AnnotationSnapping.Move(moving, [farther, nearer], Canvas, 1).Bounds.X);
        Assert.AreEqual(104, AnnotationSnapping.Move(moving, [nearer, farther], Canvas, 1).Bounds.X);
    }

    [TestMethod]
    public void LockedMoveAxisIsNotPulledOffItsOriginalTrack()
    {
        RectD proposed = new(103, 103, 20, 20);
        AnnotationSnapResult horizontal = AnnotationSnapping.Move(proposed, [new(100, 100, 80, 80)], Canvas, 1, allowY: false);
        AnnotationSnapResult vertical = AnnotationSnapping.Move(proposed, [new(100, 100, 80, 80)], Canvas, 1, allowX: false);
        Assert.AreEqual(new RectD(100, 103, 20, 20), horizontal.Bounds);
        Assert.AreEqual(new RectD(103, 100, 20, 20), vertical.Bounds);
        Assert.IsTrue(horizontal.Guides.All(guide => guide.Axis == SnapGuideAxis.Vertical));
    }

    [TestMethod]
    public void TargetsExcludeTheWholeSelectionHiddenObjectsAndHiddenLayers()
    {
        var document = new CaptureDocument();
        var hiddenLayer = new AnnotationLayer { IsVisible = false };
        document.Layers.Add(hiddenLayer);
        AnnotationObject Add(RectD bounds, Guid layer, bool visible = true)
        {
            var item = new AnnotationObject { Bounds = bounds, LayerId = layer, IsVisible = visible };
            document.Annotations.Add(item); return item;
        }
        AnnotationObject first = Add(new(10, 20, 30, 40), document.Layers[0].Id);
        AnnotationObject second = Add(new(40, 50, 30, 40), document.Layers[0].Id);
        Add(new(70, 80, 30, 40), document.Layers[0].Id, false);
        Add(new(100, 110, 30, 40), hiddenLayer.Id);
        Add(new(130, 140, 30, 40), document.Layers[0].Id);
        IReadOnlyList<RectD> targets = AnnotationSnapping.GetTargets(document, new HashSet<Guid> { first.Id, second.Id });
        Assert.AreEqual(new RectD(130, 140, 30, 40), targets.Single());
    }

    [TestMethod]
    public void DirectedLinesKeepTheirDirectionWhenMovedOrSnappedAtAnEndpoint()
    {
        AnnotationSnapResult moved = AnnotationSnapping.Move(new(123, 80, -20, -30), [new(100, 200, 80, 40)], Canvas, 1);
        Assert.AreEqual(new RectD(120, 80, -20, -30), moved.Bounds);
        RectD line = new(200, 200, -97, -47);
        AnnotationSnapResult endpoint = AnnotationSnapping.Resize(line, AnnotationBoundsHandle.End, [new(100, 150, 80, 60)], Canvas, 1);
        Assert.AreEqual(new RectD(200, 200, -100, -50), endpoint.Bounds);
        Assert.HasCount(2, endpoint.Guides);
    }

    [TestMethod]
    public void SideResizeOnlyMovesTheDraggedEdgeAndCannotCrossMinimumSize()
    {
        AnnotationSnapResult result = AnnotationSnapping.Resize(new(100, 100, 47, 30), AnnotationBoundsHandle.Right, [new(150, 250, 80, 60)], Canvas, 1);
        Assert.AreEqual(new RectD(100, 100, 50, 30), result.Bounds);
        Assert.AreEqual(150, result.Guides.Single().Position);
        RectD small = new(103, 100, 7, 20);
        Assert.AreEqual(small, AnnotationSnapping.Resize(small, AnnotationBoundsHandle.Left, [new(108, 250, 1, 20)], Canvas, 1).Bounds);
    }

    [TestMethod]
    [DataRow(AnnotationBoundsHandle.TopLeft, 97, 97, 97)]
    [DataRow(AnnotationBoundsHandle.TopRight, 143, 100, 97)]
    [DataRow(AnnotationBoundsHandle.BottomLeft, 97, 97, 100)]
    [DataRow(AnnotationBoundsHandle.BottomRight, 143, 100, 100)]
    public void ConstrainedCornerResizeKeepsASquareAndItsOppositeCorner(AnnotationBoundsHandle handle, int target, int x, int y)
    {
        AnnotationSnapResult result = AnnotationSnapping.Resize(new(100, 100, 40, 40), handle, [new(target, 250, 80, 60)], Canvas, 1, square: true);
        Assert.AreEqual(new RectD(x, y, 43, 43), result.Bounds);
        Assert.AreEqual(target, result.Guides.Single().Position);
    }

    [TestMethod]
    public void ConstrainedResizeDoesNotShowACompetingGuideThatWasNotReached()
    {
        AnnotationSnapResult result = AnnotationSnapping.Resize(new(100, 100, 40, 40), AnnotationBoundsHandle.BottomRight, [new(142, 144, 80, 60)], Canvas, 1, square: true);
        Assert.AreEqual(new RectD(100, 100, 42, 42), result.Bounds);
        Assert.AreEqual(SnapGuideAxis.Vertical, result.Guides.Single().Axis);
    }

    [TestMethod]
    public void SnappingDoesNotPushClippedOrOversizedGeometryFurtherOutsideTheCanvas()
    {
        RectD clipped = new(470, 80, 40, 20);
        AnnotationSnapResult result = AnnotationSnapping.Move(clipped, [new(475, 200, 80, 30)], new(500, 500), 1);
        Assert.AreEqual(clipped, result.Bounds);
        Assert.IsEmpty(result.Guides);
    }

    [TestMethod]
    public void ObjectsEntirelyOutsideTheImageCannotAttractOtherObjects()
    {
        RectD proposed = new(103, 80, 20, 20);
        AnnotationSnapResult result = AnnotationSnapping.Move(proposed, [new(100, 1500, 80, 40)], Canvas, 1);
        Assert.AreEqual(proposed, result.Bounds);
        Assert.IsEmpty(result.Guides);
    }

    [TestMethod]
    public void InvalidGeometryOrZoomDoesNotProduceGuides()
    {
        Assert.IsEmpty(AnnotationSnapping.Move(new(double.NaN, 0, 10, 10), [], Canvas, 1).Guides);
        Assert.IsEmpty(AnnotationSnapping.Move(new(1, 2, 10, 10), [], Canvas, 0).Guides);
        Assert.IsEmpty(AnnotationSnapping.Resize(new(1, 2, 10, 10), AnnotationBoundsHandle.Right, [], new(0, 0), 1).Guides);
    }
}
