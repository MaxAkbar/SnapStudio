namespace SnapStudio.Core.Diagnostics;

public interface IDiagnosticLog
{
    Task WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}

public interface ISensitiveDataRedactor
{
    string Redact(string value);

    IReadOnlyDictionary<string, string> RedactProperties(IReadOnlyDictionary<string, string> properties);
}

public interface ICrashRecoveryJournal
{
    Task WriteAsync(RecoveryJournalEntry entry, CancellationToken cancellationToken);
}
