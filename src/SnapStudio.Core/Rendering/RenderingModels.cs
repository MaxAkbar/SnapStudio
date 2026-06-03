using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Rendering;

public sealed record RenderRequest(
    DocumentId DocumentId,
    double Scale,
    RectD? Viewport);

public sealed record RenderedImage(
    int Width,
    int Height,
    byte[] Pixels,
    string PixelFormat);

public sealed record RenderResult(RenderedImage? Image, string? ErrorMessage)
{
    public bool Succeeded => Image is not null;

    public static RenderResult Success(RenderedImage image) => new(image, null);

    public static RenderResult Failed(string errorMessage) => new(null, errorMessage);
}

public sealed record RasterCropRequest(
    ImageAsset SourceImage,
    RectD SourceBounds,
    string OutputDirectory);

public sealed record RasterResizeRequest(
    ImageAsset SourceImage,
    int Width,
    int Height,
    string OutputDirectory);

public sealed record RasterEditResult(ImageAsset? Image, string? ErrorMessage)
{
    public bool Succeeded => Image is not null;

    public static RasterEditResult Success(ImageAsset image) => new(image, null);

    public static RasterEditResult Failed(string errorMessage) => new(null, errorMessage);
}
