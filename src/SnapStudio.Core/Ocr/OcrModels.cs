using System.Text.Json.Serialization;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Ocr;

public enum OcrFailureReason
{
    NotEnabled,
    Unsupported,
    ProviderUnavailable,
    ImageUnavailable,
    RecognitionFailed,
    Unknown
}

public sealed record OcrProviderStatus(
    bool IsAvailable,
    string ProviderName,
    IReadOnlyCollection<string> SupportedLanguageTags,
    string? UnavailableReason)
{
    public static OcrProviderStatus Available(
        string providerName,
        IReadOnlyCollection<string> supportedLanguageTags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(supportedLanguageTags);

        return new OcrProviderStatus(
            true,
            providerName,
            supportedLanguageTags,
            null);
    }

    public static OcrProviderStatus Unavailable(
        string providerName,
        string unavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(unavailableReason);

        return new OcrProviderStatus(
            false,
            providerName,
            [],
            unavailableReason);
    }
}

public sealed record OcrRequest(
    DocumentId DocumentId,
    ImageAsset SourceImage,
    RectD? SourceRegion,
    string? LanguageTag = null);

public sealed record OcrTextExtractionRequest(
    DocumentId DocumentId,
    ImageAsset SourceImage,
    RectD? SourceRegion,
    string? LanguageTag = null);

public sealed record OcrFailure(
    OcrFailureReason Reason,
    string Message,
    Exception? Exception = null);

public sealed record OcrRecognitionResult(
    OcrResult? Result,
    OcrFailure? Failure)
{
    public bool Succeeded => Result is not null;

    public static OcrRecognitionResult Success(OcrResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new OcrRecognitionResult(result, null);
    }

    public static OcrRecognitionResult Failed(OcrFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new OcrRecognitionResult(null, failure);
    }
}

public sealed record OcrTextExtractionResult(
    OcrResult? Result,
    OcrFailure? Failure,
    bool WasFromCache)
{
    public bool Succeeded => Result is not null;

    public static OcrTextExtractionResult Success(
        OcrResult result,
        bool wasFromCache)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new OcrTextExtractionResult(result, null, wasFromCache);
    }

    public static OcrTextExtractionResult Failed(OcrFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new OcrTextExtractionResult(null, failure, false);
    }
}

public sealed record OcrResult(
    Guid Id,
    DocumentId DocumentId,
    DateTimeOffset RecognizedAtUtc,
    RectD? SourceRegion,
    string LanguageTag,
    IReadOnlyList<OcrTextLine> Lines)
{
    [JsonIgnore]
    public bool HasText => Lines.Count > 0;

    [JsonIgnore]
    public string Text => string.Join(Environment.NewLine, Lines.Select(line => line.Text));
}

public sealed record OcrTextLine(
    string Text,
    RectD Bounds,
    IReadOnlyList<OcrTextWord> Words);

public sealed record OcrTextWord(
    string Text,
    RectD Bounds,
    double? Confidence);

public sealed record OcrCacheDocument(
    DocumentId DocumentId,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<OcrResult> Results)
{
    public static OcrCacheDocument Empty(DocumentId documentId) => new(documentId, default, []);
}
