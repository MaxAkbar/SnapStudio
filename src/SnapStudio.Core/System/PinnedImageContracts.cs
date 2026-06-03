using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.System;

public sealed record PinnedImageRequest(
    DocumentId DocumentId,
    string Title);

public sealed record PinnedImageResult(
    bool Succeeded,
    string? ErrorMessage)
{
    public static PinnedImageResult Success() => new(true, null);

    public static PinnedImageResult Failed(string errorMessage) => new(false, errorMessage);
}

public interface IPinnedImageService
{
    Task<PinnedImageResult> PinDocumentAsync(
        PinnedImageRequest request,
        CancellationToken cancellationToken);
}
