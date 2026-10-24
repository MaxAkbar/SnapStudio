using System.Collections.ObjectModel;

namespace SnapStudio.App.ViewModels;

internal static class CollectionUpdates
{
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
