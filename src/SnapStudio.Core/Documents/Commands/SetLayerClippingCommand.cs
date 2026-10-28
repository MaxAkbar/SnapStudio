using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public sealed class SetLayerClippingCommand(Guid layerId, LayerMask? mask, RectD? crop, string displayName) : IEditCommand
{
    private readonly LayerMask? _afterMask = mask?.DeepCopy();
    private LayerMask? _beforeMask;
    private RectD? _beforeCrop;
    private bool _captured;
    public string DisplayName => displayName;

    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnnotationLayer layer = document.Layers.FirstOrDefault(layer => layer.Id == layerId)
            ?? throw new InvalidDataException("The selected layer no longer exists.");
        var next = LayerClipping.Clone(layer); next.Mask = _afterMask; next.CropBounds = crop;
        LayerClipping.Validate(next, document.SourceImage.Width, document.SourceImage.Height);
        if (!_captured) { _beforeMask = layer.Mask?.DeepCopy(); _beforeCrop = layer.CropBounds; _captured = true; }
        layer.Mask = _afterMask?.DeepCopy(); layer.CropBounds = crop;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_captured) throw new InvalidOperationException("The layer edit has not been applied.");
        AnnotationLayer layer = document.Layers.First(layer => layer.Id == layerId);
        layer.Mask = _beforeMask?.DeepCopy(); layer.CropBounds = _beforeCrop;
        return ValueTask.CompletedTask;
    }
}
