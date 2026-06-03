using System.Text.Json;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;

namespace SnapStudio.Storage;

public sealed class FileSystemDocumentRepository : IDocumentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IClock _clock;
    private readonly string _rootPath;

    public FileSystemDocumentRepository(string rootPath, IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
        _clock = clock ?? new SystemClock();
    }

    public async Task<CaptureDocument> CreateFromCaptureAsync(
        CaptureResult capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var now = _clock.UtcNow;
        var properties = new Dictionary<string, string>(capture.Metadata)
        {
            ["captureId"] = capture.Id.ToString(),
            ["capturedAtUtc"] = capture.CapturedAtUtc.ToString("O")
        };

        var document = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = capture.SourceImage,
            Metadata = new DocumentMetadata(now, now, properties)
        };

        await SaveAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public async Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken cancellationToken)
    {
        string documentPath = GetDocumentPath(id);
        if (!File.Exists(documentPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(documentPath);
        return await JsonSerializer
            .DeserializeAsync<CaptureDocument>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        string documentDirectory = GetDocumentDirectory(document.Id);
        Directory.CreateDirectory(documentDirectory);

        string documentPath = GetDocumentPath(document.Id);
        bool isExistingDocument = File.Exists(documentPath);
        var now = _clock.UtcNow;
        DateTimeOffset createdAt = document.Metadata.CreatedAtUtc == default
            ? now
            : document.Metadata.CreatedAtUtc;
        DateTimeOffset modifiedAt = isExistingDocument && now <= document.Metadata.ModifiedAtUtc
            ? document.Metadata.ModifiedAtUtc.AddTicks(1)
            : now;

        document.Metadata = document.Metadata with
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = modifiedAt
        };

        string temporaryPath = $"{documentPath}.tmp";
        string json = JsonSerializer.Serialize(document, JsonOptions);

        await File
            .WriteAllTextAsync(temporaryPath, json, cancellationToken)
            .ConfigureAwait(false);

        File.Move(temporaryPath, documentPath, overwrite: true);
    }

    public Task<bool> DeleteAsync(DocumentId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string documentDirectory = GetDocumentDirectory(id);
        if (!Directory.Exists(documentDirectory))
        {
            return Task.FromResult(false);
        }

        Directory.Delete(documentDirectory, recursive: true);

        return Task.FromResult(true);
    }

    private string GetDocumentDirectory(DocumentId id) => Path.Combine(_rootPath, id.ToString());

    private string GetDocumentPath(DocumentId id) => Path.Combine(
        GetDocumentDirectory(id),
        FileSystemDocumentNames.DocumentFileName);
}
