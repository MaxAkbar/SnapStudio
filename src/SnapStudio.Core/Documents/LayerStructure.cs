namespace SnapStudio.Core.Documents;

public static class LayerStructure
{
    public static AnnotationObject Clone(AnnotationObject item) => new()
    {
        Id = item.Id, Kind = item.Kind, LayerId = item.LayerId, GroupId = item.GroupId, ClipRegions = item.ClipRegions?.ToList(),
        IsVisible = item.IsVisible, Bounds = item.Bounds, Text = item.Text, StrokePoints = [.. item.StrokePoints],
        FillRectangles = [.. item.FillRectangles], Image = item.Image, Vector = item.Vector, Style = item.Style
    };
    public static AnnotationObjectGroup Clone(AnnotationObjectGroup group) => new() { Id = group.Id, LayerId = group.LayerId, Name = group.Name, IsVisible = group.IsVisible };

    public static IReadOnlyList<AnnotationObjectGroup> DuplicateGroups(CaptureDocument document, IReadOnlyList<AnnotationObject> originals, IReadOnlyList<AnnotationObject> copies)
    {
        if (originals.Count != copies.Count) throw new ArgumentException("Each original needs a matching copy.");
        var copiedIds = originals.Select(item => item.Id).ToHashSet(); var groups = new Dictionary<Guid, AnnotationObjectGroup>();
        for (int index = 0; index < originals.Count; index++)
        {
            copies[index].GroupId = null;
            if (originals[index].GroupId is not Guid id) continue;
            var group = document.ObjectGroups.Single(item => item.Id == id);
            if (!document.Annotations.Where(item => item.GroupId == id).All(item => copiedIds.Contains(item.Id)))
            {
                copies[index].IsVisible &= group.IsVisible;
                continue;
            }
            if (!groups.TryGetValue(id, out var copy))
            {
                copy = Clone(group); copy.Id = Guid.NewGuid(); copy.Name += " copy"; groups[id] = copy;
            }
            copies[index].GroupId = copy.Id;
        }
        return groups.Values.ToArray();
    }

    public static IEnumerable<AnnotationLayer> PaintLayers(CaptureDocument document)
    {
        foreach (var root in document.Layers.Where(layer => layer.FolderId is null))
        {
            if (!root.IsFolder) yield return root;
            else foreach (var child in document.Layers.Where(layer => layer.FolderId == root.Id)) yield return child;
        }
    }
    public static bool IsVisible(CaptureDocument document, AnnotationLayer layer) => layer.IsVisible
        && (layer.FolderId is null || document.Layers.Any(folder => folder.Id == layer.FolderId && folder.IsFolder && folder.IsVisible));

    public static IEnumerable<AnnotationObject> PaintObjects(CaptureDocument document, Guid layerId)
    {
        var objects = document.Annotations.Where(item => item.LayerId == layerId).ToArray(); var done = new HashSet<Guid>();
        foreach (var item in objects)
        {
            if (item.GroupId is not Guid group) { yield return item; continue; }
            if (done.Add(group)) foreach (var member in objects.Where(member => member.GroupId == group)) yield return member;
        }
    }
    public static IEnumerable<Guid> ExpandObjectGroups(CaptureDocument document, IEnumerable<Guid> ids)
    {
        var chosen = ids.ToHashSet(); var groups = document.Annotations.Where(item => chosen.Contains(item.Id) && item.GroupId is not null).Select(item => item.GroupId).ToHashSet();
        return document.Annotations.Where(item => chosen.Contains(item.Id) || item.GroupId is not null && groups.Contains(item.GroupId)).Select(item => item.Id);
    }
    public static void NormalizeOrder(CaptureDocument document)
    {
        var layers = document.Layers.Where(layer => layer.FolderId is null).SelectMany(root => root.IsFolder
            ? new[] { root }.Concat(document.Layers.Where(child => child.FolderId == root.Id)) : [root]).ToList();
        var objects = PaintLayers(document).SelectMany(layer => PaintObjects(document, layer.Id)).ToList();
        document.Layers = layers; document.Annotations = objects;
    }
    public static void Validate(CaptureDocument document)
    {
        if (document.ObjectGroups is null) throw new InvalidDataException("The object groups are missing.");
        if (!document.Layers.Any(layer => !layer.IsFolder)) throw new InvalidDataException("Keep at least one editable layer.");
        foreach (var layer in document.Layers)
        {
            if (layer.IsFolder && (layer.FolderId is not null || layer.Mask is not null || layer.CropBounds is not null))
                throw new InvalidDataException("Folders cannot contain other folders or have layer masks.");
            if (layer.FolderId is Guid parent && !document.Layers.Any(folder => folder.Id == parent && folder.IsFolder && folder.FolderId is null))
                throw new InvalidDataException("A layer folder is missing or invalid.");
        }
        var groups = new HashSet<Guid>();
        foreach (var group in document.ObjectGroups)
            if (group is null || group.Id == Guid.Empty || !groups.Add(group.Id) || string.IsNullOrWhiteSpace(group.Name)
                || !document.Layers.Any(layer => layer.Id == group.LayerId && !layer.IsFolder))
                throw new InvalidDataException("An object group is invalid or belongs to a missing layer.");
        foreach (var item in document.Annotations)
        {
            if (document.Layers.Any(layer => layer.Id == item.LayerId && layer.IsFolder)) throw new InvalidDataException("Add objects to a layer, not a folder.");
            if (item.GroupId is Guid group && !document.ObjectGroups.Any(candidate => candidate.Id == group && candidate.LayerId == item.LayerId))
                throw new InvalidDataException("Object groups can only contain objects from the same layer.");
            ObjectClipping.Validate(item);
        }
    }
}
