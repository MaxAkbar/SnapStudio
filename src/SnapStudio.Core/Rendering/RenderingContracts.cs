namespace SnapStudio.Core.Rendering;

public interface IDocumentRenderer
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken);
}

public interface IDocumentRasterEditor
{
    Task<RegionFillResult> FindFillRegionAsync(RegionFillRequest request, CancellationToken cancellationToken);

    Task<RasterEditResult> CropAsync(
        RasterCropRequest request,
        CancellationToken cancellationToken);

    Task<RasterEditResult> ResizeAsync(
        RasterResizeRequest request,
        CancellationToken cancellationToken);
}
