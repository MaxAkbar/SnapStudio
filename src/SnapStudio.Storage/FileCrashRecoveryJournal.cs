using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnapStudio.Core.Diagnostics;

namespace SnapStudio.Storage;

public sealed class FileCrashRecoveryJournal : ICrashRecoveryJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _journalPath;
    private readonly ISensitiveDataRedactor _redactor;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileCrashRecoveryJournal(string journalPath, ISensitiveDataRedactor? redactor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);

        _journalPath = Path.GetFullPath(journalPath);
        _redactor = redactor ?? new DefaultSensitiveDataRedactor();
    }

    public async Task WriteAsync(RecoveryJournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string? directory = Path.GetDirectoryName(_journalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var redactedEntry = entry with
        {
            Message = _redactor.Redact(entry.Message),
            DocumentTitle = entry.DocumentTitle is null
                ? null
                : _redactor.Redact(entry.DocumentTitle),
            Properties = _redactor.RedactProperties(entry.Properties)
        };

        string jsonLine = JsonSerializer.Serialize(redactedEntry, JsonOptions);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File
                .AppendAllTextAsync(_journalPath, $"{jsonLine}{Environment.NewLine}", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
