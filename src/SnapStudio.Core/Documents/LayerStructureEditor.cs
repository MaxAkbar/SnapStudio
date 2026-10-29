namespace SnapStudio.Core.Documents;

public sealed record LayerStructureResult(Guid? LayerId = null, Guid? GroupId = null, IReadOnlyList<Guid>? ObjectIds = null);

public static class LayerStructureEditor
{
    private static string Name(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim()[..Math.Min(80, name.Trim().Length)];
    private static AnnotationLayer Layer(CaptureDocument document, Guid id) => document.Layers.FirstOrDefault(layer => layer.Id == id)
        ?? throw new InvalidOperationException("The layer no longer exists.");
    private static AnnotationObject[] Objects(CaptureDocument document, IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet(); var objects = DocumentLayers.InPaintOrder(document).Where(item => selected.Contains(item.Id)).ToArray();
        if (objects.Length != selected.Count || objects.Length == 0) throw new InvalidOperationException("Select objects to edit.");
        return objects;
    }
    public static LayerStructureResult GroupLayers(CaptureDocument document, IEnumerable<Guid> ids, string name)
    {
        var selected = ids.Distinct().Select(id => Layer(document, id)).ToArray();
        if (selected.Length < 2 || selected.Any(layer => layer.IsFolder)) throw new InvalidOperationException("Select at least two layers. Folders cannot be grouped.");
        var folder = new AnnotationLayer { Name = Name(name, "Layer group"), IsFolder = true };
        int position = document.Layers.FindIndex(layer => selected.Contains(layer));
        foreach (var layer in selected) layer.IsVisible = LayerStructure.IsVisible(document, layer);
        document.Layers.Insert(position, folder); foreach (var layer in selected) layer.FolderId = folder.Id;
        return new(folder.Id);
    }
    public static LayerStructureResult UngroupFolder(CaptureDocument document, Guid id)
    {
        var folder = Layer(document, id); if (!folder.IsFolder) throw new InvalidOperationException("Select a layer folder.");
        var children = document.Layers.Where(layer => layer.FolderId == id).ToArray();
        int index = document.Layers.IndexOf(folder); document.Layers.Remove(folder);
        foreach (var child in children) { document.Layers.Remove(child); child.IsVisible &= folder.IsVisible; child.FolderId = null; document.Layers.Insert(Math.Min(index++, document.Layers.Count), child); }
        return new(children.LastOrDefault()?.Id);
    }
    public static LayerStructureResult GroupObjects(CaptureDocument document, IEnumerable<Guid> ids, string name, bool merge = false)
    {
        var objects = Objects(document, ids);
        if (objects.Length < 2 || objects.Select(item => item.LayerId).Distinct().Count() != 1)
            throw new InvalidOperationException("Select at least two objects from the same layer.");
        if (!merge && objects.Any(item => item.GroupId is not null)) throw new InvalidOperationException("Object groups cannot be grouped again. Ungroup them or use Merge.");
        var oldGroups = objects.Select(item => item.GroupId).OfType<Guid>().ToHashSet();
        var group = new AnnotationObjectGroup { Name = Name(name, merge ? "Merged objects" : "Object group"), LayerId = objects[0].LayerId!.Value };
        int index = document.Annotations.FindIndex(item => item.Id == objects[^1].Id);
        foreach (var item in objects) document.Annotations.Remove(item);
        index = Math.Min(index - objects.Length + 1, document.Annotations.Count); index = Math.Max(0, index);
        document.Annotations.InsertRange(index, objects);
        foreach (var item in objects)
        {
            if (merge && item.GroupId is Guid previous && document.ObjectGroups.FirstOrDefault(candidate => candidate.Id == previous) is { IsVisible: false }) item.IsVisible = false;
            item.GroupId = group.Id;
        }
        document.ObjectGroups.RemoveAll(old => oldGroups.Contains(old.Id) && !document.Annotations.Any(item => item.GroupId == old.Id));
        document.ObjectGroups.Add(group); return new(group.LayerId, group.Id, objects.Select(item => item.Id).ToArray());
    }
    public static LayerStructureResult UngroupObjects(CaptureDocument document, IEnumerable<Guid> ids)
    {
        var groups = ids.ToHashSet(); var selected = document.ObjectGroups.Where(group => groups.Contains(group.Id)).ToArray();
        if (selected.Length != groups.Count || selected.Length == 0) throw new InvalidOperationException("Select an object group.");
        var objects = document.Annotations.Where(item => item.GroupId is Guid group && groups.Contains(group)).ToArray();
        foreach (var item in objects)
        {
            if (selected.Any(group => group.Id == item.GroupId && !group.IsVisible)) item.IsVisible = false;
            item.GroupId = null;
        }
        document.ObjectGroups.RemoveAll(group => groups.Contains(group.Id)); return new(selected[0].LayerId, ObjectIds: objects.Select(item => item.Id).ToArray());
    }
    public static LayerStructureResult MoveObjects(CaptureDocument document, IEnumerable<Guid> ids, Guid targetId)
    {
        var target = Layer(document, targetId); if (target.IsFolder) throw new InvalidOperationException("Move objects into a layer, not a folder.");
        var objects = Objects(document, ids); var chosen = objects.Select(item => item.Id).ToHashSet();
        foreach (var group in document.ObjectGroups.Where(group => objects.Any(item => item.GroupId == group.Id)).ToArray())
        {
            var members = document.Annotations.Where(item => item.GroupId == group.Id).ToArray();
            if (members.All(item => chosen.Contains(item.Id))) group.LayerId = target.Id;
            else foreach (var member in members.Where(item => chosen.Contains(item.Id))) { member.IsVisible &= group.IsVisible; member.GroupId = null; }
        }
        foreach (var item in objects) item.LayerId = target.Id;
        return new(target.Id, ObjectIds: objects.Select(item => item.Id).ToArray());
    }
    public static LayerStructureResult Merge(CaptureDocument document, IEnumerable<Guid> layerIds, IEnumerable<Guid> objectIds, string name)
    {
        var requested = layerIds.Distinct().Select(id => Layer(document, id)).ToArray(); var requestedFolders = requested.Where(layer => layer.IsFolder).Select(layer => layer.Id).ToHashSet();
        var layers = requested.Where(layer => !layer.IsFolder).Concat(document.Layers.Where(layer => layer.FolderId is Guid folder && requestedFolders.Contains(folder))).Distinct().ToArray();
        var ids = objectIds.ToHashSet(); foreach (var item in document.Annotations.Where(item => layers.Any(layer => layer.Id == item.LayerId))) ids.Add(item.Id);
        if (requested.Length == 0)
        {
            var objects = Objects(document, ids);
            if (objects.Select(item => item.LayerId).Distinct().Count() == 1) return GroupObjects(document, ids, name, merge: true);
        }
        if (layers.Length == 0 && ids.Count < 2 || requested.Length == 1 && !requested[0].IsFolder && ids.Count == 0)
            throw new InvalidOperationException("Select multiple layers or objects to merge.");
        var selected = document.Annotations.Where(item => ids.Contains(item.Id)).ToArray();
        if (selected.Length != ids.Count) throw new InvalidOperationException("A selected object no longer exists.");
        var merged = new AnnotationLayer { Name = Name(name, "Merged layer") };
        var origins = layers.Concat(selected.Select(item => Layer(document, item.LayerId!.Value))).Distinct().ToArray();
        var parents = origins.Select(layer => layer.FolderId).Distinct().ToArray();
        if (parents.Length == 1 && parents[0] is Guid folder && !requestedFolders.Contains(folder)) merged.FolderId = folder;
        int insertion = document.Layers.FindLastIndex(layer => origins.Contains(layer) || requestedFolders.Contains(layer.Id));
        var sourceOrder = DocumentLayers.InPaintOrder(document).Where(item => ids.Contains(item.Id)).ToArray();
        var groupIds = selected.Select(item => item.GroupId).OfType<Guid>().Concat(document.ObjectGroups.Where(group => layers.Any(layer => layer.Id == group.LayerId)).Select(group => group.Id)).ToHashSet();
        foreach (var group in document.ObjectGroups.Where(group => groupIds.Contains(group.Id)))
        {
            if (document.Annotations.Where(item => item.GroupId == group.Id).All(item => ids.Contains(item.Id))) group.LayerId = merged.Id;
            else foreach (var item in selected.Where(item => item.GroupId == group.Id)) { item.IsVisible &= group.IsVisible; item.GroupId = null; }
        }
        foreach (var item in selected)
        {
            var origin = Layer(document, item.LayerId!.Value); ObjectClipping.RetainLayerClip(document, item, origin);
            item.IsVisible &= LayerStructure.IsVisible(document, origin); item.LayerId = merged.Id;
        }
        foreach (var item in selected) document.Annotations.Remove(item);
        document.Annotations.AddRange(sourceOrder);
        document.Layers.Insert(Math.Min(insertion + 1, document.Layers.Count), merged);
        var removedIds = layers.Select(layer => layer.Id).Concat(requestedFolders).ToHashSet(); document.Layers.RemoveAll(layer => removedIds.Contains(layer.Id));
        return new(merged.Id, ObjectIds: selected.Select(item => item.Id).ToArray());
    }
    public static LayerStructureResult MoveLayersToFolder(CaptureDocument document, IEnumerable<Guid> ids, Guid? folderId)
    {
        if (folderId is Guid target && !Layer(document, target).IsFolder) throw new InvalidOperationException("Choose a layer folder.");
        var layers = ids.Distinct().Select(id => Layer(document, id)).ToArray();
        if (layers.Length == 0 || layers.Any(layer => layer.IsFolder)) throw new InvalidOperationException("Folders cannot be grouped or placed inside other folders.");
        foreach (var layer in layers) layer.FolderId = folderId;
        return new(layers[^1].Id);
    }
    public static LayerStructureResult DeleteLayers(CaptureDocument document, IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet(); foreach (var id in selected.ToArray()) Layer(document, id);
        foreach (var child in document.Layers.Where(layer => layer.FolderId is Guid folder && selected.Contains(folder))) selected.Add(child.Id);
        if (!document.Layers.Any(layer => !layer.IsFolder && !selected.Contains(layer.Id))) throw new InvalidOperationException("Keep at least one editable layer.");
        document.Annotations.RemoveAll(item => item.LayerId is Guid id && selected.Contains(id));
        document.ObjectGroups.RemoveAll(group => selected.Contains(group.LayerId)); document.Layers.RemoveAll(layer => selected.Contains(layer.Id));
        return new(document.Layers.LastOrDefault(layer => !layer.IsFolder)?.Id);
    }
    public static LayerStructureResult MoveLayer(CaptureDocument document, Guid id, int direction)
    {
        var layer = Layer(document, id); if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var siblings = document.Layers.Where(item => item.FolderId == layer.FolderId).ToArray(); int index = Array.IndexOf(siblings, layer), target = index + direction;
        if (target < 0 || target >= siblings.Length) throw new InvalidOperationException("The layer is at the end of its folder.");
        var adjacent = siblings[target]; document.Layers.Remove(layer);
        int insertion = document.Layers.IndexOf(adjacent) + (direction > 0 ? 1 : 0); document.Layers.Insert(insertion, layer);
        return new(layer.Id);
    }
}
