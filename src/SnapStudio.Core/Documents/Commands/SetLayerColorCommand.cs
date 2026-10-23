namespace SnapStudio.Core.Documents;

public sealed class SetLayerColorCommand(Guid layerId, LayerColorLabel color) : IEditCommand
{
    private LayerColorLabel? _originalColor;

    public string DisplayName => "Set Layer Color";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(color))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        AnnotationLayer layer = FindLayer(document);
        _originalColor ??= layer.ColorLabel;
        layer.ColorLabel = color;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        FindLayer(document).ColorLabel = _originalColor
            ?? throw new InvalidOperationException("The layer color has not been changed.");
        return ValueTask.CompletedTask;
    }

    private AnnotationLayer FindLayer(CaptureDocument document) => document.Layers
        .FirstOrDefault(layer => layer.Id == layerId)
        ?? throw new InvalidOperationException("The layer could not be found.");
}
