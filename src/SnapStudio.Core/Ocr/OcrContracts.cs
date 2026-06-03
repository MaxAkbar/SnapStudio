using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Ocr;

public interface IOcrProvider
{
    Task<OcrProviderStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<OcrRecognitionResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken);
}

public interface IOcrResultCache
{
    Task<OcrCacheDocument> GetAsync(
        DocumentId documentId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        OcrCacheDocument cacheDocument,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        DocumentId documentId,
        CancellationToken cancellationToken);
}

public interface IOcrTextExtractionService
{
    Task<OcrTextExtractionResult> ExtractAsync(
        OcrTextExtractionRequest request,
        CancellationToken cancellationToken);

    Task InvalidateAsync(DocumentId documentId, CancellationToken cancellationToken);
}
