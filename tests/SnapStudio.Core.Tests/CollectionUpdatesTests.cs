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
}
