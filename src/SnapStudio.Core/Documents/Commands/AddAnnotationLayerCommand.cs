namespace SnapStudio.Core.Documents;

public sealed class AddAnnotationLayerCommand(AnnotationLayer layer) : IEditCommand
{
    public string DisplayName => "Add Layer";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.Layers.All(candidate => candidate.Id != layer.Id))
        {
            document.Layers.Add(layer);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        document.Layers.RemoveAll(candidate => candidate.Id == layer.Id);
        return ValueTask.CompletedTask;
    }
}
