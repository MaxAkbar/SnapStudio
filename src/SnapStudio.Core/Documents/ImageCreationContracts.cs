using SnapStudio.Core.Capture;

namespace SnapStudio.Core.Documents;

public interface IImageCreationService
{
    Task<CaptureOutcome> CreateBlankAsync(NewImageRequest request, CancellationToken cancellationToken);

    Task<CaptureOutcome> PasteAsync(CancellationToken cancellationToken);
}

public sealed record NewImageRequest(int Width, int Height)
{
    public const int MaximumDimension = 20000;
    public const long MaximumPixelCount = 40000000;

    public static string? GetValidationError(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height)
            || width < 1 || height < 1
            || width > MaximumDimension || height > MaximumDimension
            || width != Math.Truncate(width) || height != Math.Truncate(height))
        {
            return $"Enter whole-number dimensions between 1 and {MaximumDimension:N0} pixels.";
        }

        return width * height > MaximumPixelCount
            ? "The image must contain no more than 40 million pixels."
            : null;
    }
}
