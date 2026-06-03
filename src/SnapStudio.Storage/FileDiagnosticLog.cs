using System.Text.Encodings.Web;
using System.Text.Json;
using SnapStudio.Core.Diagnostics;

namespace SnapStudio.Storage;

public sealed class FileDiagnosticLog : IDiagnosticLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _logPath;
    private readonly ISensitiveDataRedactor _redactor;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileDiagnosticLog(string logPath, ISensitiveDataRedactor? redactor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        _logPath = Path.GetFullPath(logPath);
        _redactor = redactor ?? new DefaultSensitiveDataRedactor();
    }

    public async Task WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        string? directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var redactedEvent = diagnosticEvent with
        {
            Message = _redactor.Redact(diagnosticEvent.Message),
            Properties = _redactor.RedactProperties(diagnosticEvent.Properties)
        };

        string jsonLine = JsonSerializer.Serialize(redactedEvent, JsonOptions);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File
                .AppendAllTextAsync(_logPath, $"{jsonLine}{Environment.NewLine}", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
