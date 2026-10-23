using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public sealed class SetLayerColorCommand : IEditCommand
{
    private readonly Guid _layerId;
    private readonly LayerColorLabel _color;
    private readonly ColorRgba? _customColor;
    private LayerColorLabel? _originalColor;
    private ColorRgba? _originalCustomColor;

    public SetLayerColorCommand(Guid layerId, LayerColorLabel color)
    {
        _layerId = layerId;
        _color = color;
    }

    public SetLayerColorCommand(Guid layerId, ColorRgba customColor)
        : this(layerId, LayerColorLabel.None)
    {
        _customColor = customColor with { A = 255 };
    }

    public string DisplayName => "Set Layer Color";

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(_color))
        {
            throw new ArgumentOutOfRangeException("color");
        }

        AnnotationLayer layer = FindLayer(document);
        if (_originalColor is null)
        {
            _originalColor = layer.ColorLabel;
            _originalCustomColor = layer.CustomColor;
        }

        layer.ColorLabel = _color;
        layer.CustomColor = _customColor;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        AnnotationLayer layer = FindLayer(document);
        layer.ColorLabel = _originalColor
            ?? throw new InvalidOperationException("The layer color has not been changed.");
        layer.CustomColor = _originalCustomColor;
        return ValueTask.CompletedTask;
    }

    private AnnotationLayer FindLayer(CaptureDocument document) => document.Layers
        .FirstOrDefault(layer => layer.Id == _layerId)
        ?? throw new InvalidOperationException("The layer could not be found.");
}
