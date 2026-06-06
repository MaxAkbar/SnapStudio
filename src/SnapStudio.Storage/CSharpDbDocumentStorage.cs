using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CSharpDB.Data;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;

namespace SnapStudio.Storage;

public sealed class CSharpDbDocumentRepository : IDocumentRepository
{
    private readonly CSharpDbDocumentStore _store;

    public CSharpDbDocumentRepository(CSharpDbDocumentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public Task<CaptureDocument> CreateFromCaptureAsync(
        CaptureResult capture,
        CancellationToken cancellationToken) => _store.CreateFromCaptureAsync(capture, cancellationToken);

    public Task<CaptureDocument?> GetAsync(
        DocumentId id,
        CancellationToken cancellationToken) => _store.GetAsync(id, cancellationToken);

    public Task SaveAsync(
        CaptureDocument document,
        CancellationToken cancellationToken) => _store.SaveAsync(document, cancellationToken);

    public Task<bool> DeleteAsync(
        DocumentId id,
        CancellationToken cancellationToken) => _store.DeleteAsync(id, cancellationToken);
}

public sealed class CSharpDbDocumentCatalog : IDocumentCatalog
{
    private readonly CSharpDbDocumentStore _store;

    public CSharpDbDocumentCatalog(CSharpDbDocumentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public Task<IReadOnlyList<DocumentSummary>> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken) => _store.GetRecentAsync(maximumCount, cancellationToken);
}

public sealed class CSharpDbDocumentStore
{
    private const string DatabaseFileName = "snapstudio-documents.db";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IClock _clock;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private readonly string _storageRoot;
    private bool _schemaInitialized;

    public CSharpDbDocumentStore(string storageRoot, IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);

        _storageRoot = Path.GetFullPath(storageRoot);
        _databasePath = Path.Combine(_storageRoot, DatabaseFileName);
        _clock = clock ?? new SystemClock();
    }

    public string DatabasePath => _databasePath;

    public async Task<CaptureDocument> CreateFromCaptureAsync(
        CaptureResult capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);

        DateTimeOffset now = _clock.UtcNow;
        var properties = new Dictionary<string, string>(capture.Metadata)
        {
            ["captureId"] = capture.Id.ToString(),
            ["capturedAtUtc"] = capture.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture)
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

    public async Task<CaptureDocument?> GetAsync(
        DocumentId id,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        await using CSharpDbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT document_json FROM documents WHERE id = @id";
        AddParameter(command, "@id", id.ToString());

        await using DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string documentJson = reader.GetString(0);
        try
        {
            return JsonSerializer.Deserialize<CaptureDocument>(documentJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        CaptureDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        await using CSharpDbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        bool isExistingDocument = await DocumentExistsAsync(
            connection,
            document.Id,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _clock.UtcNow;
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

        string title = ResolveTitle(document);
        string sourceKind = DocumentSummaryFactory.ResolveSourceKind(document.Metadata.Properties);
        string documentJson = JsonSerializer.Serialize(document, JsonOptions);

        await using DbTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM documents WHERE id = @id",
            [new("@id", document.Id.ToString())],
            cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            INSERT INTO documents (
                id,
                created_at_utc,
                modified_at_utc,
                title,
                source_image_path,
                annotation_count,
                source_kind,
                document_json
            ) VALUES (
                @id,
                @createdAtUtc,
                @modifiedAtUtc,
                @title,
                @sourceImagePath,
                @annotationCount,
                @sourceKind,
                @documentJson
            )
            """,
            [
                new("@id", document.Id.ToString()),
                new("@createdAtUtc", FormatDateTimeOffset(document.Metadata.CreatedAtUtc)),
                new("@modifiedAtUtc", FormatDateTimeOffset(document.Metadata.ModifiedAtUtc)),
                new("@title", title),
                new("@sourceImagePath", document.SourceImage.Path),
                new("@annotationCount", document.Annotations.Count),
                new("@sourceKind", sourceKind),
                new("@documentJson", documentJson)
            ],
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        DocumentId id,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        await using CSharpDbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        bool exists = await DocumentExistsAsync(connection, id, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            return false;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            "DELETE FROM documents WHERE id = @id",
            [new("@id", id.ToString())],
            cancellationToken).ConfigureAwait(false);

        DeleteThumbnailCacheFiles(id);
        return true;
    }

    public async Task<IReadOnlyList<DocumentSummary>> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            return [];
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        await using CSharpDbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                title,
                created_at_utc,
                modified_at_utc,
                source_image_path,
                annotation_count,
                source_kind
            FROM documents
            ORDER BY modified_at_utc DESC, created_at_utc DESC
            """;

        var summaries = new List<DocumentSummary>();
        await using DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (summaries.Count < maximumCount
            && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            summaries.Add(CreateSummary(reader));
        }

        return summaries;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaInitialized)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaInitialized)
            {
                return;
            }

            Directory.CreateDirectory(_storageRoot);

            await using CSharpDbConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                connection,
                transaction: null,
                """
                CREATE TABLE IF NOT EXISTS documents (
                    id TEXT PRIMARY KEY,
                    created_at_utc TEXT NOT NULL,
                    modified_at_utc TEXT NOT NULL,
                    title TEXT NOT NULL,
                    source_image_path TEXT NOT NULL,
                    annotation_count INTEGER NOT NULL,
                    source_kind TEXT NOT NULL,
                    document_json TEXT NOT NULL
                )
                """,
                [],
                cancellationToken).ConfigureAwait(false);

            _schemaInitialized = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private CSharpDbConnection CreateConnection()
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Data Source"] = _databasePath
        };

        return new CSharpDbConnection(builder.ConnectionString);
    }

    private async Task<bool> DocumentExistsAsync(
        DbConnection connection,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM documents WHERE id = @id";
        AddParameter(command, "@id", id.ToString());

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string commandText,
        IReadOnlyCollection<SqlParameterValue> parameters,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;

        foreach (SqlParameterValue parameter in parameters)
        {
            AddParameter(command, parameter.Name, parameter.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private DocumentSummary CreateSummary(DbDataReader reader)
    {
        var id = new DocumentId(Guid.Parse(reader.GetString(0)));
        DateTimeOffset modifiedAtUtc = ParseDateTimeOffset(reader.GetString(3));
        string documentDirectory = Path.Combine(_storageRoot, id.ToString());
        string? thumbnailPath = ResolveThumbnailPath(documentDirectory, modifiedAtUtc);

        return new DocumentSummary(
            id,
            reader.GetString(1),
            ParseDateTimeOffset(reader.GetString(2)),
            modifiedAtUtc,
            reader.GetString(4),
            Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
            thumbnailPath,
            reader.GetString(6));
    }

    private static string ResolveTitle(CaptureDocument document)
    {
        return document.Metadata.Properties.TryGetValue("title", out string? value)
            ? value
            : $"Capture {document.Metadata.CreatedAtUtc:yyyy-MM-dd HH:mm}";
    }

    private static string? ResolveThumbnailPath(
        string documentDirectory,
        DateTimeOffset modifiedAtUtc)
    {
        string exactThumbnailPath = Path.Combine(
            documentDirectory,
            FileSystemDocumentNames.CreateThumbnailFileName(modifiedAtUtc));
        if (File.Exists(exactThumbnailPath))
        {
            return exactThumbnailPath;
        }

        try
        {
            return Directory
                .EnumerateFiles(documentDirectory, FileSystemDocumentNames.ThumbnailFileSearchPattern)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void DeleteThumbnailCacheFiles(DocumentId id)
    {
        string documentDirectory = Path.Combine(_storageRoot, id.ToString());
        if (!Directory.Exists(documentDirectory))
        {
            return;
        }

        foreach (string thumbnailPath in Directory.EnumerateFiles(
            documentDirectory,
            FileSystemDocumentNames.ThumbnailFileSearchPattern))
        {
            File.Delete(thumbnailPath);
        }
    }

    private static string FormatDateTimeOffset(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private readonly record struct SqlParameterValue(string Name, object Value);
}
