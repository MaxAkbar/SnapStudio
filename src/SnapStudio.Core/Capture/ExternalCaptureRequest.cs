using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Capture;

/// <summary>Versioned, physical-screen-pixel request used by ExactFrame.</summary>
public sealed record ExternalCaptureRequest(int ProtocolVersion, Guid RequestId, int X, int Y, int Width, int Height, string? Name)
{
    public RectD Bounds => new(X, Y, Width, Height);
    public void Validate()
    {
        if (ProtocolVersion != 1 || RequestId == Guid.Empty) throw new InvalidDataException("Unsupported capture request.");
        if (Width < 4 || Height < 4 || Width > NewImageRequest.MaximumDimension || Height > NewImageRequest.MaximumDimension
            || (long)Width * Height > NewImageRequest.MaximumPixelCount
            || (long)X + Width > int.MaxValue || (long)Y + Height > int.MaxValue)
            throw new InvalidDataException("The frame dimensions are outside SnapStudio's image limits.");
        if (Name?.Length > 120 || Name?.Any(char.IsControl) == true) throw new InvalidDataException("The frame name is invalid.");
    }
}

public sealed record ExternalCaptureResponse(int ProtocolVersion, Guid RequestId, bool Succeeded, string? Error, Guid? DocumentId = null);
