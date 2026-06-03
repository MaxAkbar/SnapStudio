namespace SnapStudio.Core.Rendering;

public interface IDocumentRenderer
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken);
}

public interface IDocumentRasterEditor
{
    Task<RasterEditResult> CropAsync(
        RasterCropRequest request,
        CancellationToken cancellationToken);

    Task<RasterEditResult> ResizeAsync(
        RasterResizeRequest request,
        CancellationToken cancellationToken);
}
