namespace SnapStudio.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Trace,
    Information,
    Warning,
    Error,
    Critical
}

public enum RecoveryJournalEventKind
{
    SessionStarted,
    SessionClosed,
    UnhandledException,
    UnobservedTaskException,
    DocumentOpened,
    DocumentSaved,
    DocumentCleared
}

public sealed record DiagnosticEvent(
    DateTimeOffset OccurredAtUtc,
    DiagnosticSeverity Severity,
    string Source,
    string Message,
    IReadOnlyDictionary<string, string> Properties)
{
    public static DiagnosticEvent Create(
        DiagnosticSeverity severity,
        string source,
        string message,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            severity,
            source,
            message,
            properties ?? new Dictionary<string, string>());
    }
}

public sealed record RecoveryJournalEntry(
    DateTimeOffset OccurredAtUtc,
    string SessionId,
    RecoveryJournalEventKind EventKind,
    string Message,
    string? DocumentId,
    string? DocumentTitle,
    DateTimeOffset? DocumentModifiedAtUtc,
    int? AnnotationCount,
    IReadOnlyDictionary<string, string> Properties)
{
    public static RecoveryJournalEntry Create(
        string sessionId,
        RecoveryJournalEventKind eventKind,
        string message,
        string? documentId = null,
        string? documentTitle = null,
        DateTimeOffset? documentModifiedAtUtc = null,
        int? annotationCount = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new RecoveryJournalEntry(
            DateTimeOffset.UtcNow,
            sessionId,
            eventKind,
            message,
            documentId,
            documentTitle,
            documentModifiedAtUtc,
            annotationCount,
            properties ?? new Dictionary<string, string>());
    }
}
