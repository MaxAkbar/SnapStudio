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
    private readonly SemaphoreSlim _saveGate = new(1, 1);

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

        await using var stream = new FileStream(
            documentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        return await JsonSerializer
            .DeserializeAsync<CaptureDocument>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        string documentDirectory = GetDocumentDirectory(document.Id);
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

        // Capture the edit before yielding: the color picker can mutate the same document
        // while an earlier save is writing. Commit these snapshots one at a time.
        string json = JsonSerializer.Serialize(document, JsonOptions);
        string temporaryPath = $"{documentPath}.{Guid.NewGuid():N}.tmp";

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(documentDirectory);
            await File
                .WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(documentPath))
            {
                // ReplaceFile supports readers that share deletion; MoveFileEx cannot
                // overwrite an open destination on Windows, even with that sharing mode.
                File.Replace(temporaryPath, documentPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, documentPath);
            }
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup must not hide the original save or cancellation failure.
            }

            throw;
        }
        finally
        {
            _saveGate.Release();
        }
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
