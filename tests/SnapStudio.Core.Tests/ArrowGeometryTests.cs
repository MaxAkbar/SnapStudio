using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ArrowGeometryTests
{
    [TestMethod]
    [DataRow(100, 0, 1)]
    [DataRow(-100, 0, 6)]
    [DataRow(0, 100, 6)]
    [DataRow(0, -100, 6)]
    [DataRow(80, -60, 16)]
    [DataRow(-80, 60, 16)]
    [DataRow(4, 0, 16)]
    public void Outline_HasAnExactTipAndStaysBetweenTheEndpoints(int dx, int dy, int thickness)
    {
        var start = new PointD(40, 50);
        var end = new PointD(start.X + dx, start.Y + dy);
        IReadOnlyList<PointD> points = ArrowGeometry.CreateOutline(start, end, thickness);
        Assert.AreEqual(end, points[3]);
        double length = Math.Sqrt(dx * dx + dy * dy);
        foreach (PointD point in points)
        {
            double distance = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length;
            Assert.IsInRange(-0.000001, length + 0.000001, distance);
        }
        Assert.IsTrue(points.Where(point => point != end).All(point =>
            ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length < length));
    }

    [TestMethod]
    public void Outline_ScalesWithZoomIncludingTheHead()
    {
        IReadOnlyList<PointD> original = ArrowGeometry.CreateOutline(new PointD(4, 8), new PointD(84, 28), 1);
        foreach (double scale in new[] { 0.25, 2.0, 4.0 })
        {
            IReadOnlyList<PointD> scaled = ArrowGeometry.CreateOutline(
                new PointD(4 * scale, 8 * scale), new PointD(84 * scale, 28 * scale), scale, scale);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.AreEqual(original[i].X * scale, scaled[i].X, 0.000001);
                Assert.AreEqual(original[i].Y * scale, scaled[i].Y, 0.000001);
            }
        }
    }

    [TestMethod]
    public void Outline_ZeroLengthDoesNotProduceInvalidGeometry()
    {
        Assert.IsEmpty(ArrowGeometry.CreateOutline(new PointD(4, 4), new PointD(4, 4), 6));
    }
}
