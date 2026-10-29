using System.Collections.ObjectModel;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly HashSet<Guid> _selectedLayerIds = [];
    private readonly HashSet<string> _collapsedTreeRows = [];
    private readonly Dictionary<string, LayerTreeRow> _layerTreeRowCache = [];
    private string? _treeSelectionAnchor;
    private readonly HashSet<Guid> _treeObjectSelection = [];
    private Guid? _selectedObjectGroupId, _cutDocumentId, _treeDocumentId;
    private Guid[] _cutObjectIds = [];
    public ObservableCollection<LayerTreeRow> LayerTreeRows { get; } = [];
    public IReadOnlyList<AnnotationLayerItem> ObjectDestinationLayers => AnnotationLayers.Where(layer => !layer.IsFolder).ToArray();
    public IReadOnlySet<Guid> SelectedLayerIds => _selectedLayerIds;
    public bool CanGroupLayers => _currentDocument is { } document && _selectedLayerIds.Count > 1 && !HasSelectedAnnotation
        && document.Layers.Where(layer => _selectedLayerIds.Contains(layer.Id)).All(layer => !layer.IsFolder);
    public bool CanGroupObjects => _currentDocument is { } document && _selectedLayerIds.Count == 0 && SelectedAnnotationCount > 1
        && document.Annotations.Where(item => SelectedAnnotationIds.Contains(item.Id)).Select(item => item.LayerId).Distinct().Count() == 1
        && document.Annotations.Where(item => SelectedAnnotationIds.Contains(item.Id)).All(item => item.GroupId is null);
    public bool CanMergeLayerTree => _selectedLayerIds.Count + SelectedAnnotationCount > 1
        || _currentDocument?.Layers.Any(layer => _selectedLayerIds.Contains(layer.Id) && layer.IsFolder && _currentDocument.Layers.Any(child => child.FolderId == layer.Id)) == true;
    public bool CanPasteCutObjects => _currentDocument is { } document && document.Id.Value == _cutDocumentId && _cutObjectIds.Length > 0
        && _cutObjectIds.All(id => document.Annotations.Any(item => item.Id == id));

    private void RefreshLayerTree()
    {
        var document = _currentDocument;
        if (_treeDocumentId != document?.Id.Value)
        {
            _selectedLayerIds.Clear(); _selectedObjectGroupId = null; _treeSelectionAnchor = null; _collapsedTreeRows.Clear(); _treeObjectSelection.Clear();
            _cutDocumentId = null; _cutObjectIds = []; _treeDocumentId = document?.Id.Value;
            _layerTreeRowCache.Clear(); LayerTreeRows.Clear();
        }
        if (document is null) { LayerTreeRows.Clear(); _selectedLayerIds.Clear(); _selectedObjectGroupId = null; return; }
        if (HasSelectedAnnotation && !_treeObjectSelection.SetEquals(SelectedAnnotationIds)) _selectedLayerIds.Clear();
        if (_selectedObjectGroupId is Guid selectedGroup && (document.ObjectGroups.All(group => group.Id != selectedGroup)
            || HasSelectedAnnotation && !document.Annotations.Where(item => item.GroupId == selectedGroup).Select(item => item.Id).ToHashSet().SetEquals(SelectedAnnotationIds))) _selectedObjectGroupId = null;
        _selectedLayerIds.RemoveWhere(id => document.Layers.All(layer => layer.Id != id));
        var rows = new List<LayerTreeRow>(); var layers = AnnotationLayers.ToDictionary(layer => layer.Id); var objects = AnnotationLayers.SelectMany(layer => layer.Objects).ToDictionary(item => item.Id);
        bool Expanded(LayerTreeRowKind kind, Guid id) => !_collapsedTreeRows.Contains(kind + ":" + id);
        void AddLayer(AnnotationLayer layer, int depth)
        {
            if (!layers.TryGetValue(layer.Id, out var item)) return;
            var kind = layer.IsFolder ? LayerTreeRowKind.Folder : LayerTreeRowKind.Layer;
            bool expanded = Expanded(kind, layer.Id);
            rows.Add(new(layer.Id, kind, layer.Name, layer.IsFolder ? $"{document.Layers.Count(child => child.FolderId == layer.Id)} layers" : item.ObjectCount,
                depth, _selectedLayerIds.Contains(layer.Id), expanded, layer.IsVisible, Layer: item));
            if (!expanded) return;
            if (layer.IsFolder) { foreach (var child in document.Layers.Where(child => child.FolderId == layer.Id).Reverse()) AddLayer(child, depth + 1); return; }
            var shown = new HashSet<Guid>();
            void AddObject(AnnotationObject annotation, int objectDepth)
            {
                if (objects.TryGetValue(annotation.Id, out var overlay)) rows.Add(new(annotation.Id, LayerTreeRowKind.Object, overlay.LayerLabel, overlay.Kind.ToString(), objectDepth,
                    SelectedAnnotationIds.Contains(annotation.Id), false, annotation.IsVisible, Object: overlay));
            }
            void AddGroup(AnnotationObjectGroup group)
            {
                var members = document.Annotations.Where(annotation => annotation.GroupId == group.Id).ToArray();
                bool open = Expanded(LayerTreeRowKind.ObjectGroup, group.Id), selected = _selectedObjectGroupId == group.Id || members.Length > 0 && members.All(annotation => SelectedAnnotationIds.Contains(annotation.Id));
                rows.Add(new(group.Id, LayerTreeRowKind.ObjectGroup, group.Name, $"{members.Length} objects", depth + 1, selected, open, group.IsVisible));
                if (open) foreach (var member in members.Reverse()) AddObject(member, depth + 2);
            }
            foreach (var annotation in LayerStructure.PaintObjects(document, layer.Id).Reverse())
            {
                if (annotation.GroupId is not Guid group) AddObject(annotation, depth + 1);
                else if (shown.Add(group)) AddGroup(document.ObjectGroups.Single(item => item.Id == group));
            }
            foreach (var group in document.ObjectGroups.Where(group => group.LayerId == layer.Id && !shown.Contains(group.Id))) AddGroup(group);
        }
        foreach (var layer in document.Layers.Where(layer => layer.FolderId is null).Reverse()) AddLayer(layer, 0);
        var liveKeys = document.Layers.Select(layer => (layer.IsFolder ? LayerTreeRowKind.Folder : LayerTreeRowKind.Layer) + ":" + layer.Id)
            .Concat(document.ObjectGroups.Select(group => LayerTreeRowKind.ObjectGroup + ":" + group.Id))
            .Concat(document.Annotations.Select(item => LayerTreeRowKind.Object + ":" + item.Id)).ToHashSet();
        foreach (string key in _layerTreeRowCache.Keys.Where(key => !liveKeys.Contains(key)).ToArray()) _layerTreeRowCache.Remove(key);
        CollectionUpdates.SynchronizeByKey(LayerTreeRows, rows, _layerTreeRowCache, row => row.Key, (row, next) => row.UpdateFrom(next));
        OnPropertyChanged(nameof(CanGroupLayers)); OnPropertyChanged(nameof(CanGroupObjects)); OnPropertyChanged(nameof(CanMergeLayerTree));
        OnPropertyChanged(nameof(CanPasteCutObjects)); OnPropertyChanged(nameof(ObjectDestinationLayers));
    }
    public void SetTreeExpanded(LayerTreeRow row)
    {
        if (row.IsExpanded) _collapsedTreeRows.Add(row.Key); else _collapsedTreeRows.Remove(row.Key); RefreshLayerTree();
    }
    public void SelectTreeRow(LayerTreeRow row, bool toggle = false, bool range = false)
    {
        if (_currentDocument is not { } document) return;
        var targets = new[] { row };
        if (range && _treeSelectionAnchor is { } anchor)
        {
            int first = LayerTreeRows.ToList().FindIndex(item => item.Key == anchor), last = LayerTreeRows.ToList().FindIndex(item => item.Key == row.Key);
            if (first >= 0 && last >= 0) targets = LayerTreeRows.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1).Where(item => item.Kind == row.Kind).ToArray();
        }
        else _treeSelectionAnchor = row.Key;
        if (!toggle) { _selectedLayerIds.Clear(); _annotationSelection.Clear(); _selectedObjectGroupId = null; }
        foreach (var target in targets)
        {
            if (target.Kind is LayerTreeRowKind.Layer or LayerTreeRowKind.Folder)
            {
                if (toggle && !_selectedLayerIds.Add(target.Id)) _selectedLayerIds.Remove(target.Id); else _selectedLayerIds.Add(target.Id);
                if (target.Kind == LayerTreeRowKind.Layer) _layerWorkspace.SelectLayer(document, target.Id);
            }
            else
            {
                var ids = target.Kind == LayerTreeRowKind.Object ? new[] { target.Id } : document.Annotations.Where(item => item.GroupId == target.Id).Select(item => item.Id).ToArray();
                _annotationSelection.SelectMany(document, ids, toggle ? AnnotationSelectionMode.Toggle : AnnotationSelectionMode.Add);
                if (target.Kind == LayerTreeRowKind.ObjectGroup) _selectedObjectGroupId = target.Id;
            }
        }
        _treeObjectSelection.Clear(); _treeObjectSelection.UnionWith(SelectedAnnotationIds); RefreshSelection();
    }
    public void EnsureTreeContextSelection(LayerTreeRow row)
    {
        if (!row.IsSelected) SelectTreeRow(row);
    }
    private async Task ApplyLayerStructureAsync(string name, Func<CaptureDocument, LayerStructureResult> edit, CancellationToken token)
    {
        if (_currentDocument is not { } document || _editCommandStack is null) return;
        LayerStructureResult? result = null;
        await _editCommandStack.ExecuteAsync(document, new LayerStructureCommand(name, planned => result = edit(planned)), token);
        _selectedLayerIds.Clear(); _selectedObjectGroupId = result?.GroupId; _annotationSelection.Clear();
        if (result?.LayerId is Guid layer)
        {
            if (document.Layers.Any(item => item.Id == layer && !item.IsFolder)) _layerWorkspace.SelectLayer(document, layer);
            if (result.ObjectIds is not { Count: > 0 } && result.GroupId is null) _selectedLayerIds.Add(layer);
        }
        if (result?.ObjectIds is { Count: > 0 } ids) _annotationSelection.SelectMany(document, ids);
        RefreshSelection(); await SaveCurrentDocumentAsync(name + " complete.", token);
    }
    public Task GroupTreeSelectionAsync(CancellationToken token)
    {
        var layers = _selectedLayerIds.ToArray(); var objects = SelectedAnnotationIds.ToArray();
        if (CanGroupLayers) return ApplyLayerStructureAsync("Group Layers", document => LayerStructureEditor.GroupLayers(document, layers, "Layer group"), token);
        if (CanGroupObjects) return ApplyLayerStructureAsync("Group Objects", document => LayerStructureEditor.GroupObjects(document, objects, "Object group"), token);
        return Task.CompletedTask;
    }
    public Task MergeTreeSelectionAsync(CancellationToken token)
    {
        var layers = _selectedLayerIds.ToArray(); var objects = SelectedAnnotationIds.ToArray();
        return ApplyLayerStructureAsync("Merge Selection", document => LayerStructureEditor.Merge(document, layers, objects, layers.Length == 0 && objects.Length > 0 ? "Merged objects" : "Merged layer"), token);
    }
    public Task UngroupTreeRowAsync(LayerTreeRow row, CancellationToken token) => ApplyLayerStructureAsync("Ungroup", document => row.Kind == LayerTreeRowKind.Folder
        ? LayerStructureEditor.UngroupFolder(document, row.Id) : LayerStructureEditor.UngroupObjects(document, [row.Id]), token);
    public Task AddTreeLayerAsync(Guid? folderId, CancellationToken token) => ApplyLayerStructureAsync("Add Layer", document =>
    {
        var layer = new AnnotationLayer { Name = "Layer " + (document.Layers.Count(item => !item.IsFolder) + 1), FolderId = folderId };
        document.Layers.Add(layer); return new(layer.Id);
    }, token);
    public Task MoveTreeObjectsAsync(Guid target, CancellationToken token)
    {
        var ids = SelectedAnnotationIds.ToArray(); return ApplyLayerStructureAsync("Move Objects to Layer", document => LayerStructureEditor.MoveObjects(document, ids, target), token);
    }
    public Task MoveTreeLayersToFolderAsync(Guid? target, CancellationToken token)
    {
        var ids = _selectedLayerIds.ToArray(); return ApplyLayerStructureAsync("Move Layers to Folder", document => LayerStructureEditor.MoveLayersToFolder(document, ids, target), token);
    }
    public Task MoveTreeLayerAsync(Guid id, int direction, CancellationToken token) => ApplyLayerStructureAsync("Arrange Layer", document => LayerStructureEditor.MoveLayer(document, id, direction), token);
    public void CutTreeObjects()
    {
        if (_currentDocument is null || !HasSelectedAnnotation || _selectedLayerIds.Count > 0) return;
        _cutDocumentId = _currentDocument.Id.Value; _cutObjectIds = SelectedAnnotationIds.ToArray(); StatusText = "Objects cut. Select a destination layer and choose Paste objects.";
        OnPropertyChanged(nameof(CanPasteCutObjects));
    }
    public async Task PasteTreeObjectsAsync(Guid target, CancellationToken token)
    {
        if (!CanPasteCutObjects || _currentDocument is null) return;
        var ids = _cutObjectIds.ToArray();
        await ApplyLayerStructureAsync("Move Cut Objects", document => LayerStructureEditor.MoveObjects(document, ids, target), token);
        _cutObjectIds = []; _cutDocumentId = null; OnPropertyChanged(nameof(CanPasteCutObjects));
    }
    public Task ToggleObjectGroupAsync(Guid id, CancellationToken token) => ApplyLayerStructureAsync("Toggle Object Group", document =>
    {
        var group = document.ObjectGroups.Single(item => item.Id == id); group.IsVisible = !group.IsVisible; return new(group.LayerId, group.Id);
    }, token);
    public Task RenameObjectGroupAsync(Guid id, string name, CancellationToken token) => ApplyLayerStructureAsync("Rename Object Group", document =>
    {
        var group = document.ObjectGroups.Single(item => item.Id == id); group.Name = string.IsNullOrWhiteSpace(name) ? "Object group" : name.Trim(); return new(group.LayerId, group.Id);
    }, token);

    public Task DeleteObjectGroupAsync(Guid id, CancellationToken token) => ApplyLayerStructureAsync("Delete Object Group", document =>
    {
        var group = document.ObjectGroups.Single(item => item.Id == id);
        document.Annotations.RemoveAll(item => item.GroupId == id); document.ObjectGroups.Remove(group); return new(group.LayerId);
    }, token);

    private void RevealTreeObject(AnnotationObject? annotation)
    {
        if (annotation?.LayerId is not Guid layerId || _currentDocument is not { } document) return;
        _collapsedTreeRows.Remove(LayerTreeRowKind.Layer + ":" + layerId);
        if (document.Layers.FirstOrDefault(layer => layer.Id == layerId)?.FolderId is Guid folder) _collapsedTreeRows.Remove(LayerTreeRowKind.Folder + ":" + folder);
        if (annotation.GroupId is Guid group) _collapsedTreeRows.Remove(LayerTreeRowKind.ObjectGroup + ":" + group);
    }
}
