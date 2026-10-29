using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SnapStudio.App.ViewModels;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CollectionUpdatesTests
{
    [TestMethod]
    public void Synchronize_UnchangedItemsRaiseNoCollectionEvents()
    {
        object first = new(), second = new();
        var items = new ObservableCollection<object> { first, second };
        int events = 0;
        items.CollectionChanged += (_, _) => events++;
        CollectionUpdates.Synchronize(items, new[] { first, second });
        Assert.AreEqual(0, events);
        Assert.AreSame(first, items[0]);
        Assert.AreSame(second, items[1]);
    }

    [TestMethod]
    public void Synchronize_ReorderMovesExistingItemsWithoutResetOrRemoval()
    {
        object first = new(), second = new(), third = new();
        var items = new ObservableCollection<object> { first, second, third };
        var changes = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, args) => changes.Add(args.Action);
        CollectionUpdates.Synchronize(items, new[] { third, first, second });
        Assert.AreSequenceEqual(new[] { third, first, second }, items);
        Assert.AreSequenceEqual(new[] { NotifyCollectionChangedAction.Move }, changes);
    }

    [TestMethod]
    public void Synchronize_AddRemoveAndFilterRetainSurvivingItems()
    {
        object first = new(), second = new(), third = new(), added = new();
        var items = new ObservableCollection<object> { first, second, third };
        var changes = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, args) =>
        {
            changes.Add(args.Action);
            Assert.Contains(second, items);
            Assert.Contains(third, items);
        };
        CollectionUpdates.Synchronize(items, new[] { second, added, third });
        Assert.AreSequenceEqual(new[] { second, added, third }, items);
        Assert.AreSequenceEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, changes);
    }

    [TestMethod]
    public void KeyedRefreshUpdatesRowValuesWithoutCollectionChanges()
    {
        var rows = new ObservableCollection<TreeRow>(); var cache = new Dictionary<string, TreeRow>();
        Refresh(rows, cache, new("layer", "Layer 1"), new("object", "Rectangle", 1));
        var layer = rows[0]; var child = rows[1]; int changes = 0;
        rows.CollectionChanged += (_, _) => changes++;
        for (int index = 0; index < 100; index++)
            Refresh(rows, cache, new("layer", "Renamed layer"), new("object", $"Rectangle {index}", 1));
        Assert.AreEqual(0, changes); Assert.AreSame(layer, rows[0]); Assert.AreSame(child, rows[1]);
        Assert.AreEqual("Renamed layer", layer.Name); Assert.AreEqual("Rectangle 99", child.Name);
    }

    [TestMethod]
    public void CollapsingAndExpandingOnlyChangesChildrenAndReusesTheirRows()
    {
        var rows = new ObservableCollection<TreeRow>(); var cache = new Dictionary<string, TreeRow>();
        Refresh(rows, cache, new("folder", "Folder"), new("layer", "Layer", 1), new("object", "Rectangle", 2), new("other", "Other layer"));
        var folder = rows[0]; var layer = rows[1]; var child = rows[2]; var other = rows[3];
        var changes = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, args) =>
        {
            changes.Add(args.Action);
            Assert.Contains(folder, rows); Assert.Contains(other, rows);
        };
        Refresh(rows, cache, new("folder", "Folder"), new("other", "Other layer"));
        Assert.AreSequenceEqual(new[] { folder, other }, rows);
        Assert.AreSequenceEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove }, changes);
        changes.Clear();
        Refresh(rows, cache, new("folder", "Folder"), new("layer", "Renamed while collapsed", 1), new("object", "Rectangle", 2), new("other", "Other layer"));
        Assert.AreSame(layer, rows[1]); Assert.AreSame(child, rows[2]); Assert.AreEqual("Renamed while collapsed", layer.Name);
        Assert.AreSequenceEqual(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add }, changes);
    }

    [TestMethod]
    public void ReparentingAndReorderingKeepAllSurvivingRowsPresent()
    {
        var rows = new ObservableCollection<TreeRow>(); var cache = new Dictionary<string, TreeRow>();
        Refresh(rows, cache, new("first", "First layer"), new("object", "Rectangle", 1), new("second", "Second layer"));
        var originals = rows.ToArray(); var changes = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, args) =>
        {
            changes.Add(args.Action);
            foreach (var original in originals) Assert.Contains(original, rows);
        };
        Refresh(rows, cache, new("first", "First layer"), new("second", "Second layer"), new("object", "Rectangle", 2));
        Assert.AreSequenceEqual(new[] { originals[0], originals[2], originals[1] }, rows);
        Assert.AreEqual(2, originals[1].Depth);
        Assert.AreSequenceEqual(new[] { NotifyCollectionChangedAction.Move }, changes);
    }

    private static void Refresh(ObservableCollection<TreeRow> rows, Dictionary<string, TreeRow> cache, params TreeRow[] desired) =>
        CollectionUpdates.SynchronizeByKey(rows, desired, cache, row => row.Id, (row, next) => { row.Name = next.Name; row.Depth = next.Depth; });

    private sealed class TreeRow(string id, string name, int depth = 0)
    {
        public string Id { get; } = id;
        public string Name { get; set; } = name;
        public int Depth { get; set; } = depth;
    }
}
