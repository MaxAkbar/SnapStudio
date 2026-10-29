using System.Collections.ObjectModel;

namespace SnapStudio.App.ViewModels;

internal static class CollectionUpdates
{
    // Keep rows alive while collapsed, and update their values without replacing their containers.
    public static void SynchronizeByKey<T, TKey>(ObservableCollection<T> collection, IReadOnlyList<T> desired,
        IDictionary<TKey, T> cache, Func<T, TKey> key, Action<T, T> update) where TKey : notnull
    {
        var retained = new List<T>(desired.Count);
        foreach (T next in desired)
        {
            TKey id = key(next);
            if (cache.TryGetValue(id, out T? item)) update(item, next);
            else cache.Add(id, item = next);
            retained.Add(item);
        }
        Synchronize(collection, retained);
    }

    // Preserve item containers, focus and scroll state instead of issuing a collection Reset.
    public static void Synchronize<T>(ObservableCollection<T> collection, IReadOnlyList<T> desired)
    {
        var retained = desired.ToHashSet();
        for (int index = collection.Count - 1; index >= 0; index--)
            if (!retained.Contains(collection[index])) collection.RemoveAt(index);
        for (int index = 0; index < desired.Count; index++)
        {
            T item = desired[index];
            if (index < collection.Count && EqualityComparer<T>.Default.Equals(collection[index], item)) continue;
            int previous = collection.IndexOf(item);
            if (previous >= 0) collection.Move(previous, index);
            else collection.Insert(index, item);
        }
    }
}
