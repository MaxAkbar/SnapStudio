using SnapStudio.Core.Ocr;

namespace SnapStudio.Platform.Windows;

public sealed class UnavailableOcrProvider : IOcrProvider
{
    public const string DefaultUnavailableReason =
        "OCR requires a packaged Windows build with package identity.";

    private const string ProviderName = "Windows OCR";
    private readonly string _unavailableReason;

    public UnavailableOcrProvider(string? unavailableReason = null)
    {
        _unavailableReason = string.IsNullOrWhiteSpace(unavailableReason)
            ? DefaultUnavailableReason
            : unavailableReason;
    }

    public Task<OcrProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(OcrProviderStatus.Unavailable(
            ProviderName,
            _unavailableReason));
    }

    public Task<OcrRecognitionResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(OcrRecognitionResult.Failed(
            new OcrFailure(
                OcrFailureReason.Unsupported,
                _unavailableReason)));
    }
}
