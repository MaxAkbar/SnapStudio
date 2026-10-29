namespace SnapStudio.Core.Documents;

/// <summary>Plans a structural edit on a copy and restores complete structure for undo/redo.</summary>
public sealed class LayerStructureCommand(string name, Action<CaptureDocument> edit) : IEditCommand
{
    private CaptureDocument? _before, _after;
    public string DisplayName => name;
    private static CaptureDocument Copy(CaptureDocument document) => new()
    {
        SchemaVersion = document.SchemaVersion,
        SourceImage = document.SourceImage,
        Layers = document.Layers.Select(LayerClipping.Clone).ToList(),
        Annotations = document.Annotations.Select(LayerStructure.Clone).ToList(),
        ObjectGroups = document.ObjectGroups.Select(LayerStructure.Clone).ToList()
    };
    private static void Restore(CaptureDocument document, CaptureDocument state)
    {
        var copy = Copy(state); document.Layers = copy.Layers; document.Annotations = copy.Annotations; document.ObjectGroups = copy.ObjectGroups;
    }
    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_after is null)
        {
            _before = Copy(document); var planned = Copy(document); edit(planned); DocumentLayers.ValidateForSave(planned); LayerStructure.NormalizeOrder(planned); _after = planned;
        }
        Restore(document, _after); return ValueTask.CompletedTask;
    }
    public ValueTask RevertAsync(CaptureDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Restore(document, _before ?? throw new InvalidOperationException("The structural edit has not been applied.")); return ValueTask.CompletedTask;
    }
}
