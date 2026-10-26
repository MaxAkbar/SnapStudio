using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class PerspectiveGridFamilyTests
{
    private static readonly RectD View = new(0, 0, 400, 400);

    [TestMethod]
    [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public void EachModeHasOneBaseGridAndOneFamilyPerVanishingPoint(int mode)
    {
        var guides = PerspectiveGrid.Create(View, new((CanvasGridMode)mode, Radius: 40));
        Assert.AreEqual(mode + 1, guides.Select(s => s.Family).Distinct().Count());
        CollectionAssert.AreEquivalent(CanvasGridFamilies.Active((CanvasGridMode)mode).ToArray(), guides.Select(s => s.Family).Distinct().ToArray());
        var defaults = new CanvasGridFamilies();
        Assert.AreEqual(mode + 1, guides.Select(s => s.Family).Distinct().Select(f => defaults.Get(f).Color).Distinct().Count());
    }

    [TestMethod]
    [DataRow(GridGuideFamily.Grid)] [DataRow(GridGuideFamily.Left)] [DataRow(GridGuideFamily.Right)]
    [DataRow(GridGuideFamily.Top)] [DataRow(GridGuideFamily.Bottom)] [DataRow(GridGuideFamily.Center)]
    public void ChangingOneDensityChangesOnlyThatFamilyAndItsSnapTargets(GridGuideFamily family)
    {
        var sparse = With(new CanvasGridFamilies(), family, new("#123456", 70, 4));
        var dense = With(sparse, family, sparse.Get(family) with { Density = 24 });
        var before = PerspectiveGrid.Create(View, new(CanvasGridMode.FivePoint, Radius: 40), sparse);
        var after = PerspectiveGrid.Create(View, new(CanvasGridMode.FivePoint, Radius: 40), dense);
        Assert.IsTrue(after.Count(s => s.Family == family) > before.Count(s => s.Family == family));
        Assert.AreSequenceEqual(before.Where(s => s.Family != family).SelectMany(s => s.Points).ToArray(),
            after.Where(s => s.Family != family).SelectMany(s => s.Points).ToArray());
        var added = after.Where(s => s.Family == family).First(s => !before.Where(b => b.Family == family).Any(b => b.Points.SequenceEqual(s.Points)));
        PointD a = added.Points[0], b = added.Points[1], point = new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        Assert.IsNotNull(PerspectiveGrid.Find(point, after.Where(s => s.Family == family).ToArray(), .001));
    }

    [TestMethod]
    [DataRow(4)] [DataRow(5)]
    public void CurvedGuidesSplitAtTheCenterIntoTheCorrectVanishingPointFamilies(int mode)
    {
        var guides = PerspectiveGrid.Create(View, new((CanvasGridMode)mode, Radius: 40));
        Assert.IsTrue(guides.Where(s => s.Family == GridGuideFamily.Top).SelectMany(s => s.Points).All(p => p.Y <= 200 + 1e-8));
        Assert.IsTrue(guides.Where(s => s.Family == GridGuideFamily.Bottom).SelectMany(s => s.Points).All(p => p.Y >= 200 - 1e-8));
        if (mode != 5) return;
        Assert.IsTrue(guides.Where(s => s.Family == GridGuideFamily.Left).SelectMany(s => s.Points).All(p => p.X <= 200 + 1e-8));
        Assert.IsTrue(guides.Where(s => s.Family == GridGuideFamily.Right).SelectMany(s => s.Points).All(p => p.X >= 200 - 1e-8));
    }

    [TestMethod]
    public void HiddenFamilyLeavesOtherFamiliesAndTheirStylesUntouched()
    {
        var original = new CanvasGridFamilies().Normalize();
        var hidden = original with { Left = original.Get(GridGuideFamily.Left) with { Opacity = 0 } };
        var before = PerspectiveGrid.Create(View, new(CanvasGridMode.TwoPoint), original);
        var after = PerspectiveGrid.Create(View, new(CanvasGridMode.TwoPoint), hidden);
        Assert.IsFalse(after.Any(s => s.Family == GridGuideFamily.Left));
        Assert.AreSequenceEqual(before.Where(s => s.Family != GridGuideFamily.Left).SelectMany(s => s.Points).ToArray(), after.SelectMany(s => s.Points).ToArray());
        Assert.AreEqual(original.Right, hidden.Right);
    }

    private static CanvasGridFamilies With(CanvasGridFamilies settings, GridGuideFamily family, GridGuideSettings value) => family switch
    {
        GridGuideFamily.Grid => settings with { Grid = value }, GridGuideFamily.Left => settings with { Left = value },
        GridGuideFamily.Right => settings with { Right = value }, GridGuideFamily.Top => settings with { Top = value },
        GridGuideFamily.Bottom => settings with { Bottom = value }, _ => settings with { Center = value }
    };
}
