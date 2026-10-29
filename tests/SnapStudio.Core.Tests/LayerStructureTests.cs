using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class LayerStructureTests
{
    private static CaptureDocument Document(int layers = 3)
    {
        var document = new CaptureDocument { SourceImage = new("source.png", 100, 100, ImagePixelFormat.Bgra32), Layers = [] };
        for (int index = 0; index < layers; index++)
        {
            var layer = new AnnotationLayer { Name = $"Layer {index + 1}" }; document.Layers.Add(layer);
            for (int child = 0; child < 2; child++) document.Annotations.Add(new AnnotationObject
            { LayerId = layer.Id, Kind = AnnotationKind.Rectangle, Bounds = new(10 + child * 20, 10, 15, 15), Text = $"{index}:{child}" });
        }
        return document;
    }
    private static string State(CaptureDocument document) => JsonSerializer.Serialize(document);
    private static ValueTask Edit(CaptureDocument document, EditCommandStack stack, Action<CaptureDocument> edit) => stack.ExecuteAsync(document, new LayerStructureCommand("Structure", edit), default);

    [TestMethod]
    public async Task LayerFolderPreservesObjectsAndPaintOrderAndUndoesOnce()
    {
        var document = Document(); var stack = new EditCommandStack(); string before = State(document);
        Guid[] ids = document.Layers.Take(2).Select(layer => layer.Id).ToArray(); Guid[] order = DocumentLayers.InPaintOrder(document).Select(item => item.Id).ToArray();
        await Edit(document, stack, planned => LayerStructureEditor.GroupLayers(planned, ids, "Notes"));
        var folder = document.Layers.Single(layer => layer.IsFolder);
        Assert.IsNull(folder.FolderId); Assert.AreEqual("Notes", folder.Name);
        Assert.IsTrue(document.Layers.Where(layer => ids.Contains(layer.Id)).All(layer => layer.FolderId == folder.Id));
        Assert.AreSequenceEqual(order, DocumentLayers.InPaintOrder(document).Select(item => item.Id).ToArray());
        string after = State(document); await stack.UndoAsync(document, default); Assert.AreEqual(before, State(document)); Assert.IsFalse(stack.CanUndo);
        await stack.RedoAsync(document, default); Assert.AreEqual(after, State(document)); DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task LayersCanBeRegroupedFromDifferentFoldersButFoldersCannotNest()
    {
        var document = Document(4); var stack = new EditCommandStack(); Guid[] layers = document.Layers.Select(layer => layer.Id).ToArray();
        await Edit(document, stack, planned => LayerStructureEditor.GroupLayers(planned, layers[..2], "First"));
        await Edit(document, stack, planned => LayerStructureEditor.GroupLayers(planned, layers[2..], "Second"));
        Guid first = document.Layers.First(layer => layer.IsFolder).Id;
        await Edit(document, stack, planned => LayerStructureEditor.GroupLayers(planned, [layers[0], layers[3]], "Combined"));
        Assert.AreEqual(document.Layers.Single(layer => layer.Name == "Combined").Id, document.Layers.Single(layer => layer.Id == layers[0]).FolderId);
        string before = State(document);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Edit(document, stack, planned => LayerStructureEditor.GroupLayers(planned, [first, layers[1]], "Invalid")).AsTask());
        Assert.AreEqual(before, State(document)); DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task ObjectGroupsRequireOneLayerAndSelectAsAUnitWithIndividualChildSelection()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] ids = document.Annotations.Take(2).Select(item => item.Id).ToArray();
        await Edit(document, stack, planned => LayerStructureEditor.GroupObjects(planned, ids, "Callout"));
        var group = document.ObjectGroups.Single(); Assert.IsTrue(document.Annotations.Take(2).All(item => item.GroupId == group.Id));
        var selection = new AnnotationSelectionState(); selection.Select(document, ids[0]); Assert.IsTrue(selection.SelectedAnnotationIds.SetEquals(ids));
        selection.SelectMany(document, [ids[0]]); Assert.HasCount(1, selection.SelectedAnnotationIds);
        string before = State(document);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Edit(document, stack, planned => LayerStructureEditor.GroupObjects(planned, [ids[0], planned.Annotations[2].Id], "Invalid")).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Edit(document, stack, planned => LayerStructureEditor.GroupObjects(planned, ids, "Nested")).AsTask());
        Assert.AreEqual(before, State(document));
    }

    [TestMethod]
    public async Task UngroupingRestoresEditableChildrenAndPreservesHiddenState()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] ids = document.Annotations.Take(2).Select(item => item.Id).ToArray();
        var group = LayerStructureEditor.GroupObjects(document, ids, "Hidden"); document.ObjectGroups[0].IsVisible = false;
        await Edit(document, stack, planned => LayerStructureEditor.UngroupObjects(planned, [group.GroupId!.Value]));
        Assert.IsEmpty(document.ObjectGroups); Assert.IsTrue(document.Annotations.Where(item => ids.Contains(item.Id)).All(item => item.GroupId is null && !item.IsVisible));
        await stack.UndoAsync(document, default); Assert.IsFalse(document.ObjectGroups[0].IsVisible); Assert.IsTrue(document.Annotations.Take(2).All(item => item.IsVisible));
    }

    [TestMethod]
    public async Task ObjectOnlyMergeCombinesGroupsWithoutFlatteningTheirKindsOrStyles()
    {
        var document = Document(1); var stack = new EditCommandStack(); var a = document.Annotations[0]; var b = document.Annotations[1];
        a.Kind = AnnotationKind.Text; a.Text = "Editable"; b.Kind = AnnotationKind.Line; b.Style = b.Style with { StrokeThickness = 7 };
        LayerStructureEditor.GroupObjects(document, [a.Id, b.Id], "Original");
        document.Annotations.Add(new() { LayerId = a.LayerId, Bounds = new(70, 10, 10, 10) });
        Guid[] ids = document.Annotations.Select(item => item.Id).ToArray();
        await Edit(document, stack, planned => LayerStructureEditor.Merge(planned, [], ids, "Merged"));
        Assert.HasCount(1, document.Layers); Assert.HasCount(1, document.ObjectGroups); Assert.HasCount(3, document.Annotations);
        Assert.AreEqual(AnnotationKind.Text, document.Annotations[0].Kind); Assert.AreEqual("Editable", document.Annotations[0].Text);
        Assert.AreEqual(7d, document.Annotations[1].Style.StrokeThickness); Assert.IsTrue(document.Annotations.All(item => item.GroupId == document.ObjectGroups[0].Id));
    }

    [TestMethod]
    public async Task MixedLayerAndObjectMergeLeavesUnselectedContentInItsOriginalLayer()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid bottom = document.Layers[0].Id, middle = document.Layers[1].Id, top = document.Layers[2].Id;
        Guid extra = document.Annotations[2].Id, retained = document.Annotations[3].Id;
        Guid[] expected = DocumentLayers.InPaintOrder(document).Where(item => item.LayerId == bottom || item.Id == extra).Select(item => item.Id).ToArray();
        await Edit(document, stack, planned => LayerStructureEditor.Merge(planned, [bottom], [extra], "Merged layer"));
        var merged = document.Layers.Single(layer => layer.Name == "Merged layer");
        Assert.IsFalse(document.Layers.Any(layer => layer.Id == bottom)); Assert.IsTrue(document.Layers.Any(layer => layer.Id == middle)); Assert.IsTrue(document.Layers.Any(layer => layer.Id == top));
        Assert.AreEqual(middle, document.Annotations.Single(item => item.Id == retained).LayerId);
        Assert.AreSequenceEqual(expected, LayerStructure.PaintObjects(document, merged.Id).Select(item => item.Id).ToArray()); Assert.HasCount(6, document.Annotations);
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task FolderMergeIncludesEmptyGroupsAndPreservesHiddenDescendants()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] ids = document.Layers.Take(2).Select(layer => layer.Id).ToArray();
        var grouped = LayerStructureEditor.GroupLayers(document, ids, "Folder"); document.Layers.Single(layer => layer.Id == grouped.LayerId).IsVisible = false;
        document.ObjectGroups.Add(new() { LayerId = ids[0], Name = "Empty" });
        await Edit(document, stack, planned => LayerStructureEditor.Merge(planned, [grouped.LayerId!.Value], [], "Merged"));
        Assert.IsFalse(document.Layers.Any(layer => layer.IsFolder)); var merged = document.Layers.Single(layer => layer.Name == "Merged");
        Assert.AreEqual(merged.Id, document.ObjectGroups.Single().LayerId);
        Assert.IsTrue(document.Annotations.Where(item => item.LayerId == merged.Id).All(item => !DocumentLayers.IsEffectivelyVisible(document, item)));
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task PartialHiddenGroupMergeDoesNotRevealDetachedMembers()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] grouped = document.Annotations.Take(2).Select(item => item.Id).ToArray();
        LayerStructureEditor.GroupObjects(document, grouped, "Hidden"); document.ObjectGroups.Single().IsVisible = false;
        Guid other = document.Annotations[2].Id;
        await Edit(document, stack, planned => LayerStructureEditor.Merge(planned, [], [grouped[0], other], "Merged"));
        var moved = document.Annotations.Single(item => item.Id == grouped[0]); Assert.IsNull(moved.GroupId); Assert.IsFalse(moved.IsVisible);
        Assert.IsNotNull(document.Annotations.Single(item => item.Id == grouped[1]).GroupId); DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task MovingWholeGroupRetainsMembershipButMovingOneChildDetachesItAndUndoes()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] ids = document.Annotations.Take(2).Select(item => item.Id).ToArray(); Guid target = document.Layers[1].Id;
        LayerStructureEditor.GroupObjects(document, ids, "Group"); Guid group = document.ObjectGroups.Single().Id;
        string before = State(document);
        await Edit(document, stack, planned => LayerStructureEditor.MoveObjects(planned, ids, target));
        Assert.AreEqual(target, document.ObjectGroups.Single().LayerId); Assert.IsTrue(document.Annotations.Where(item => ids.Contains(item.Id)).All(item => item.GroupId == group && item.LayerId == target));
        await stack.UndoAsync(document, default); Assert.AreEqual(before, State(document));
        await Edit(document, stack, planned => LayerStructureEditor.MoveObjects(planned, [ids[0]], target));
        Assert.IsNull(document.Annotations.Single(item => item.Id == ids[0]).GroupId); Assert.AreEqual(group, document.Annotations.Single(item => item.Id == ids[1]).GroupId);
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task FolderCannotOwnObjectsOrBeAMoveDestination()
    {
        var document = Document(); var stack = new EditCommandStack(); var folder = LayerStructureEditor.GroupLayers(document, document.Layers.Take(2).Select(layer => layer.Id).ToArray(), "Folder");
        string before = State(document);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Edit(document, stack, planned => LayerStructureEditor.MoveObjects(planned, [planned.Annotations[0].Id], folder.LayerId!.Value)).AsTask());
        Assert.AreEqual(before, State(document));
        document.Annotations[0].LayerId = folder.LayerId;
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
    }

    [TestMethod]
    public async Task DeleteFolderRemovesDescendantGroupsAndRestoresReferencesAndOrder()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] ids = document.Layers.Take(2).Select(layer => layer.Id).ToArray();
        var folder = LayerStructureEditor.GroupLayers(document, ids, "Folder"); LayerStructureEditor.GroupObjects(document, document.Annotations.Take(2).Select(item => item.Id).ToArray(), "Group");
        string before = State(document); var original = document.Layers.ToArray();
        await stack.ExecuteAsync(document, new RemoveAnnotationLayerCommand(folder.LayerId!.Value), default);
        Assert.HasCount(1, document.Layers); Assert.HasCount(2, document.Annotations); Assert.IsEmpty(document.ObjectGroups); DocumentLayers.ValidateForSave(document);
        await stack.UndoAsync(document, default); Assert.AreEqual(before, State(document)); Assert.AreSame(original[0], document.Layers[0]);
        await stack.RedoAsync(document, default); Assert.HasCount(1, document.Layers);
    }

    [TestMethod]
    public async Task DeleteFolderCannotRemoveLastEditableLayer()
    {
        var document = Document(2); var stack = new EditCommandStack(); var folder = LayerStructureEditor.GroupLayers(document, document.Layers.Select(layer => layer.Id).ToArray(), "All");
        string before = State(document);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stack.ExecuteAsync(document, new RemoveAnnotationLayerCommand(folder.LayerId!.Value), default).AsTask());
        Assert.AreEqual(before, State(document)); Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task DuplicateGroupGetsNewMembershipAndOneUndoWhilePartialCopyIsDetached()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] ids = document.Annotations.Take(2).Select(item => item.Id).ToArray();
        LayerStructureEditor.GroupObjects(document, ids, "Group"); Guid group = document.ObjectGroups.Single().Id;
        var editor = new DocumentSelectionEditor(stack); var copies = await editor.DuplicateAsync(document, ids, default);
        Assert.HasCount(2, document.ObjectGroups); Assert.AreNotEqual(group, copies[0].GroupId); Assert.AreEqual(copies[0].GroupId, copies[1].GroupId);
        await stack.UndoAsync(document, default); Assert.HasCount(1, document.ObjectGroups); Assert.HasCount(6, document.Annotations);
        await stack.RedoAsync(document, default); Assert.HasCount(2, document.ObjectGroups); DocumentLayers.ValidateForSave(document);
        var partial = await editor.DuplicateAsync(document, [ids[0]], default); Assert.IsNull(partial[0].GroupId);
    }

    [TestMethod]
    public async Task FolderReorderingMovesTheWholeBlockAndUngroupPreservesOrder()
    {
        var document = Document(); var stack = new EditCommandStack(); Guid[] children = document.Layers.Take(2).Select(layer => layer.Id).ToArray(); Guid other = document.Layers[2].Id;
        var result = LayerStructureEditor.GroupLayers(document, children, "Folder"); LayerStructure.NormalizeOrder(document);
        await Edit(document, stack, planned => LayerStructureEditor.MoveLayer(planned, result.LayerId!.Value, 1));
        Assert.AreSequenceEqual(new[] { other }.Concat(children).ToArray(), LayerStructure.PaintLayers(document).Select(layer => layer.Id).ToArray());
        await Edit(document, stack, planned => LayerStructureEditor.UngroupFolder(planned, result.LayerId!.Value));
        Assert.IsTrue(document.Layers.All(layer => !layer.IsFolder && layer.FolderId is null));
        Assert.AreSequenceEqual(new[] { other }.Concat(children).ToArray(), document.Layers.Select(layer => layer.Id).ToArray());
    }

    [TestMethod]
    public async Task SavedFoldersGroupsAndRetainedClipsReopenAndDuplicateIndependently()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = Document(); Guid[] ids = document.Annotations.Take(2).Select(item => item.Id).ToArray();
        LayerStructureEditor.GroupLayers(document, document.Layers.Take(2).Select(layer => layer.Id).ToArray(), "Folder");
        LayerStructureEditor.GroupObjects(document, ids, "Group"); document.Annotations[0].ClipRegions = [new(0, 0, .5, .5)];
        var repository = new FileSystemDocumentRepository(Path.Combine(workspace.Path, "documents")); await repository.SaveAsync(document, default);
        var reopened = await repository.GetAsync(document.Id, default); Assert.IsNotNull(reopened);
        Assert.AreEqual(document.ObjectGroups[0].Id, reopened.ObjectGroups[0].Id); Assert.AreEqual(document.Layers[1].FolderId, reopened.Layers[1].FolderId);
        Assert.AreSequenceEqual(document.Annotations[0].ClipRegions!, reopened.Annotations[0].ClipRegions!);
        var duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(reopened, "Copy"); duplicate.ObjectGroups[0].Name = "Changed"; duplicate.Annotations[0].ClipRegions!.Clear();
        Assert.AreEqual("Group", reopened.ObjectGroups[0].Name); Assert.HasCount(1, reopened.Annotations[0].ClipRegions!);
        DocumentLayers.ValidateForSave(duplicate);
    }

    [TestMethod]
    public void PreviousSchemaMigratesWithNoGroupsAndRejectsMalformedMembership()
    {
        var document = Document(); document.SchemaVersion = 11; DocumentLayers.EnsureInitialized(document);
        Assert.AreEqual(12, document.SchemaVersion); Assert.IsEmpty(document.ObjectGroups);
        document.Annotations[0].GroupId = Guid.NewGuid(); Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
        document.Annotations[0].GroupId = null; document.Annotations[0].ClipRegions = [new(double.NaN, 0, 1, 1)];
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
    }

    [TestMethod]
    public async Task FailedOrCancelledStructureEditsLeaveDocumentAndUndoHistoryUntouched()
    {
        var document = Document(); string before = State(document); var stack = new EditCommandStack();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Edit(document, stack, planned => planned.Layers.Clear()).AsTask());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => stack.ExecuteAsync(document, new LayerStructureCommand("Cancelled", planned => planned.Annotations.Clear()), cancelled.Token).AsTask());
        Assert.AreEqual(before, State(document)); Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public async Task MergeRetainsMaskAndCropPixelsAndObjectClipMovesWithArtwork()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = RenderDocument(workspace.Path);
        document.Layers[1].Mask = new([new(2, 2, 10, 8)]); document.Layers[1].CropBounds = new(6, 0, 10, 6);
        Guid green = document.Annotations[1].Id; Guid[] layers = document.Layers.Select(layer => layer.Id).ToArray();
        using var before = await Render(document); var stack = new EditCommandStack();
        await Edit(document, stack, planned => LayerStructureEditor.Merge(planned, layers, [], "Merged"));
        using var after = await Render(document);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++) Assert.AreEqual(before.GetPixel(x, y).ToArgb(), after.GetPixel(x, y).ToArgb(), $"Pixel {x},{y}");
        var item = document.Annotations.Single(item => item.Id == green); Assert.IsNotNull(item.ClipRegions);
        var clip = ObjectClipping.GetRectangles(item)!.Single(); item.Bounds = item.Bounds with { X = item.Bounds.X + 2, Y = item.Bounds.Y + 1 };
        Assert.AreEqual(clip with { X = clip.X + 2, Y = clip.Y + 1 }, ObjectClipping.GetRectangles(item)!.Single());
        await stack.UndoAsync(document, default); Assert.IsNull(document.Annotations.Single(item => item.Id == green).ClipRegions); Assert.IsNotNull(document.Layers[1].Mask);
    }

    [TestMethod]
    public async Task FolderAndObjectGroupVisibilityAffectRenderingWithoutDeletingContent()
    {
        using var workspace = TemporaryWorkspace.Create(); var document = RenderDocument(workspace.Path); var green = document.Annotations[1];
        document.Annotations.Add(new() { LayerId = green.LayerId, Kind = AnnotationKind.Rectangle, Bounds = new(2, 2, 2, 2) });
        var group = LayerStructureEditor.GroupObjects(document, document.Annotations.Where(item => item.LayerId == green.LayerId).Select(item => item.Id).ToArray(), "Green");
        document.ObjectGroups.Single().IsVisible = false;
        using (var hidden = await Render(document)) Assert.AreEqual(Color.Red.ToArgb(), hidden.GetPixel(8, 8).ToArgb());
        document.ObjectGroups.Single().IsVisible = true;
        var folder = LayerStructureEditor.GroupLayers(document, document.Layers.Select(layer => layer.Id).ToArray(), "All"); document.Layers.Single(layer => layer.Id == folder.LayerId).IsVisible = false;
        Assert.IsFalse(DocumentLayers.IsEffectivelyVisible(document, green));
        using (var hidden = await Render(document)) Assert.AreEqual(Color.Blue.ToArgb(), hidden.GetPixel(8, 8).ToArgb());
        Assert.HasCount(3, document.Annotations);
    }

    [TestMethod]
    public async Task ArrangeTreatsGroupsAsBlocksAndChildrenAsGroupSiblings()
    {
        var document = Document(1); var stack = new EditCommandStack(); var editor = new DocumentSelectionEditor(stack);
        Guid[] grouped = document.Annotations.Select(item => item.Id).ToArray(); LayerStructureEditor.GroupObjects(document, grouped, "Group");
        var outside = new AnnotationObject { LayerId = document.Layers[0].Id, Bounds = new(80, 10, 10, 10) }; document.Annotations.Add(outside);
        Assert.IsTrue(await editor.ArrangeAsync(document, [outside.Id], -1, false, default));
        Assert.AreSequenceEqual(new[] { outside.Id }.Concat(grouped).ToArray(), DocumentLayers.InPaintOrder(document).Select(item => item.Id).ToArray());
        await stack.UndoAsync(document, default);
        Assert.IsTrue(await editor.ArrangeAsync(document, [grouped[1]], -1, false, default));
        Assert.AreSequenceEqual(new[] { grouped[1], grouped[0], outside.Id }, DocumentLayers.InPaintOrder(document).Select(item => item.Id).ToArray());
        DocumentLayers.ValidateForSave(document);
    }

    private static CaptureDocument RenderDocument(string directory)
    {
        string path = Path.Combine(directory, "source.png");
        using (var bitmap = new Bitmap(16, 12)) { using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Blue); bitmap.Save(path, ImageFormat.Png); }
        var document = new CaptureDocument { SourceImage = new(path, 16, 12, ImagePixelFormat.Bgra32), Layers = [new(), new()] };
        foreach (var (layer, color) in document.Layers.Zip(new[] { new ColorRgba(255, 0, 0, 255), new ColorRgba(0, 255, 0, 255) }))
        { var fill = PixelSelection.Create(16, 12, PixelSelectionTool.Rectangle, new(0, 0, 16, 12)).CreateFill(color); fill.LayerId = layer.Id; document.Annotations.Add(fill); }
        return document;
    }
    private static async Task<Bitmap> Render(CaptureDocument document)
    {
        var result = await new SystemDrawingDocumentRenderer(new Repository(document)).RenderAsync(new(document.Id, 1, null), default);
        Assert.IsTrue(result.Succeeded, result.ErrorMessage); using var stream = new MemoryStream(result.Image!.Pixels); using var image = new Bitmap(stream); return new Bitmap(image);
    }
    private sealed class Repository(CaptureDocument document) : IDocumentRepository
    {
        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken token) => Task.FromResult<CaptureDocument?>(document);
        public Task SaveAsync(CaptureDocument document, CancellationToken token) => Task.CompletedTask;
        public Task<bool> DeleteAsync(DocumentId id, CancellationToken token) => Task.FromResult(false);
        public Task<CaptureDocument> CreateFromCaptureAsync(CaptureResult capture, CancellationToken token) => throw new NotSupportedException();
    }
}
