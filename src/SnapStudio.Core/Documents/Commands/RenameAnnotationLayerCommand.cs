namespace SnapStudio.Core.Documents;

public sealed class RenameAnnotationLayerCommand(Guid layerId, string name) : IEditCommand
{
    public const int MaximumNameLength = 120;
    private string? _originalName;

    public string DisplayName => "Rename Layer";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentException($"Layer names can contain at most {MaximumNameLength} characters.", nameof(name));
        }

        AnnotationLayer layer = FindLayer(document);
        _originalName ??= layer.Name;
        layer.Name = normalized;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        FindLayer(document).Name = _originalName
            ?? throw new InvalidOperationException("The layer has not been renamed.");
        return ValueTask.CompletedTask;
    }

    private AnnotationLayer FindLayer(CaptureDocument document) => document.Layers
        .FirstOrDefault(layer => layer.Id == layerId)
        ?? throw new InvalidOperationException("The layer could not be found.");
}
