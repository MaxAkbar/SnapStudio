using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

/// <summary>Edits a selection without changing its relative geometry or creating one undo step per object.</summary>
public sealed class DocumentSelectionEditor(IEditCommandStack editStack)
{
    public ValueTask<bool> SetBoundsAsync(CaptureDocument document, IReadOnlyDictionary<Guid, RectD> bounds, string name, CancellationToken token)
    {
        AnnotationObject[] selected = Resolve(document, bounds.Keys);
        return ExecuteAsync(document, selected.Where(item => item.Bounds != bounds[item.Id])
            .Select(item => (IEditCommand)new UpdateAnnotationBoundsCommand(item.Id, item.Bounds, bounds[item.Id], name)).ToArray(), name, token);
    }

    public ValueTask<bool> RemoveAsync(CaptureDocument document, IEnumerable<Guid> ids, CancellationToken token) =>
        ExecuteAsync(document, Resolve(document, ids).Select(item => (IEditCommand)new RemoveAnnotationCommand(item)).ToArray(), "Delete Objects", token);

    public async ValueTask<IReadOnlyList<AnnotationObject>> DuplicateAsync(CaptureDocument document, IEnumerable<Guid> ids, CancellationToken token)
    {
        AnnotationObject[] selected = Resolve(document, ids);
        IReadOnlyDictionary<Guid, RectD> shifted = AnnotationBoundsEditor.MoveSelection(selected.ToDictionary(item => item.Id, item => item.Bounds), 10, 10,
            new SizeD(document.SourceImage.Width, document.SourceImage.Height));
        AnnotationObject[] copies = selected.Select(item => new AnnotationObject
        {
            Kind = item.Kind,
            LayerId = item.LayerId,
            ClipRegions = item.ClipRegions?.ToList(),
            IsVisible = item.IsVisible,
            Bounds = shifted[item.Id],
            Text = item.Text,
            Style = item.Style,
            Image = item.Image?.DeepCopy(),
            Vector = item.Vector,
            StrokePoints = [.. item.StrokePoints],
            FillRectangles = [.. item.FillRectangles]
        }).ToArray();
        var groups = LayerStructure.DuplicateGroups(document, selected, copies);
        await ExecuteAsync(document, groups.Select(group => (IEditCommand)new AddObjectGroupCommand(group))
            .Concat(copies.Select(item => (IEditCommand)new AddAnnotationCommand(item))).ToArray(), "Duplicate Objects", token).ConfigureAwait(false);
        return copies;
    }

    public ValueTask<bool> NudgeAsync(CaptureDocument document, IEnumerable<Guid> ids, double deltaX, double deltaY, CancellationToken token)
    {
        IReadOnlyDictionary<Guid, RectD> moved = AnnotationBoundsEditor.MoveSelection(Resolve(document, ids).ToDictionary(item => item.Id, item => item.Bounds),
            deltaX, deltaY, new SizeD(document.SourceImage.Width, document.SourceImage.Height));
        return SetBoundsAsync(document, moved, "Move Objects", token);
    }

    public ValueTask<bool> SetVisibilityAsync(CaptureDocument document, IEnumerable<Guid> ids, bool visible, CancellationToken token) =>
        ExecuteAsync(document, Resolve(document, ids).Where(item => item.IsVisible != visible)
            .Select(item => (IEditCommand)new UpdateAnnotationVisibilityCommand(item.Id, item.IsVisible, visible)).ToArray(), visible ? "Show Objects" : "Hide Objects", token);

    public ValueTask<bool> SetStylesAsync(CaptureDocument document, IReadOnlyDictionary<Guid, AnnotationStyle> styles, CancellationToken token,
        IReadOnlyDictionary<Guid, RectD>? fittedBounds = null)
    {
        var commands = new List<IEditCommand>();
        foreach (AnnotationObject item in Resolve(document, styles.Keys))
        {
            if (item.Style != styles[item.Id]) commands.Add(new UpdateAnnotationStyleCommand(item.Id, item.Style, styles[item.Id], "Style Objects"));
            if (fittedBounds is not null && fittedBounds.TryGetValue(item.Id, out RectD bounds) && bounds != item.Bounds)
                commands.Add(new UpdateAnnotationBoundsCommand(item.Id, item.Bounds, bounds));
        }
        return ExecuteAsync(document, commands, "Style Objects", token);
    }

    public ValueTask<bool> MoveToLayerAsync(CaptureDocument document, IEnumerable<Guid> ids, Guid layerId, CancellationToken token)
    {
        AnnotationLayer layer = document.Layers.FirstOrDefault(item => item.Id == layerId) ?? throw new InvalidDataException("The target layer is missing.");
        if (layer.IsFolder || !LayerStructure.IsVisible(document, layer)) return ValueTask.FromResult(false);
        var selection = ids.ToArray();
        if (Resolve(document, selection).All(item => item.LayerId == layerId)) return ValueTask.FromResult(false);
        return ExecuteAsync(document, [new LayerStructureCommand("Move Objects to Layer", planned => LayerStructureEditor.MoveObjects(planned, selection, layerId))], "Move Objects to Layer", token);
    }

    public ValueTask<bool> ArrangeAsync(CaptureDocument document, IEnumerable<Guid> ids, int direction, bool toEdge, CancellationToken token)
    {
        HashSet<Guid> selected = Resolve(document, ids).Select(item => item.Id).ToHashSet();
        var commands = new List<IEditCommand>();
        foreach (AnnotationLayer layer in document.Layers)
        {
            Guid[] before = document.Annotations.Where(item => item.LayerId == layer.Id).Select(item => item.Id).ToArray();
            var blocks = new List<Guid[]>(); var groups = new HashSet<Guid>();
            foreach (var item in LayerStructure.PaintObjects(document, layer.Id))
            {
                if (item.GroupId is not Guid group) blocks.Add([item.Id]);
                else if (groups.Add(group))
                {
                    var members = LayerStructure.PaintObjects(document, layer.Id).Where(member => member.GroupId == group).Select(member => member.Id).ToArray();
                    blocks.Add(Arrange(members, selected.Contains, direction, toEdge));
                }
            }
            Guid[] after = Arrange(blocks.ToArray(), block => block.All(selected.Contains), direction, toEdge).SelectMany(block => block).ToArray();
            if (!before.SequenceEqual(after)) commands.Add(new ReorderSelectionCommand(layer.Id, before, after));
        }
        return ExecuteAsync(document, commands, "Arrange Objects", token);
    }

    private static T[] Arrange<T>(T[] before, Func<T, bool> selected, int direction, bool toEdge)
    {
        if (toEdge) return (direction > 0 ? before.Where(item => !selected(item)).Concat(before.Where(selected))
            : before.Where(selected).Concat(before.Where(item => !selected(item)))).ToArray();
        T[] after = [.. before];
        if (direction > 0)
        {
            for (int index = after.Length - 2; index >= 0; index--)
                if (selected(after[index]) && !selected(after[index + 1])) (after[index], after[index + 1]) = (after[index + 1], after[index]);
        }
        else
        {
            for (int index = 1; index < after.Length; index++)
                if (selected(after[index]) && !selected(after[index - 1])) (after[index], after[index - 1]) = (after[index - 1], after[index]);
        }
        return after;
    }

    private async ValueTask<bool> ExecuteAsync(CaptureDocument document, IReadOnlyList<IEditCommand> commands, string name, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (commands.Count == 0) return false;
        await editStack.ExecuteAsync(document, commands.Count == 1 ? commands[0] : new CompositeEditCommand(name, commands), token).ConfigureAwait(false);
        return true;
    }

    private static AnnotationObject[] Resolve(CaptureDocument document, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(document);
        HashSet<Guid> selection = ids.ToHashSet();
        AnnotationObject[] items = document.Annotations.Where(item => selection.Contains(item.Id)).ToArray();
        if (items.Length != selection.Count) throw new InvalidDataException("A selected object is no longer in this document.");
        return items;
    }

    private sealed class ReorderSelectionCommand(Guid layerId, Guid[] before, Guid[] after) : IEditCommand
    {
        public string DisplayName => "Arrange Objects";
        public ValueTask ApplyAsync(CaptureDocument document, CancellationToken token) => Reorder(document, before, after, token);
        public ValueTask RevertAsync(CaptureDocument document, CancellationToken token) => Reorder(document, after, before, token);
        private ValueTask Reorder(CaptureDocument document, Guid[] expected, Guid[] order, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int[] slots = document.Annotations.Select((item, index) => (item, index)).Where(entry => entry.item.LayerId == layerId).Select(entry => entry.index).ToArray();
            if (!slots.Select(index => document.Annotations[index].Id).SequenceEqual(expected)) throw new InvalidOperationException("The stacking order has changed.");
            var items = slots.Select(index => document.Annotations[index]).ToDictionary(item => item.Id);
            for (int index = 0; index < slots.Length; index++) document.Annotations[slots[index]] = items[order[index]];
            return ValueTask.CompletedTask;
        }
    }
}
