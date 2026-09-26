namespace SnapStudio.Core.Documents;

public sealed class UpdateLayerVisibilityCommand(Guid layerId, bool before, bool after) : IEditCommand
{
    public string DisplayName => after ? "Show Layer" : "Hide Layer";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        SetVisibility(document, after, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        SetVisibility(document, before, cancellationToken);
        return ValueTask.CompletedTask;
    }

    private void SetVisibility(
        CaptureDocument document,
        bool isVisible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        AnnotationLayer layer = document.Layers
            .FirstOrDefault(candidate => candidate.Id == layerId)
            ?? throw new InvalidOperationException("The layer could not be found.");
        layer.IsVisible = isVisible;
    }
}
