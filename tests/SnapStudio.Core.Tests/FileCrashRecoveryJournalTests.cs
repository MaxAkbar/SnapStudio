using SnapStudio.Core.Diagnostics;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class FileCrashRecoveryJournalTests
{
    [TestMethod]
    public async Task WriteAsync_AppendsRedactedRecoveryEntry()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string journalPath = Path.Combine(workspace.Path, "logs", "recovery.jsonl");
        var journal = new FileCrashRecoveryJournal(journalPath);

        await journal.WriteAsync(
            RecoveryJournalEntry.Create(
                "session-1",
                RecoveryJournalEventKind.DocumentSaved,
                @"Saved C:\Users\maxim\Pictures\capture.png for user@example.com.",
                documentId: "document-1",
                documentTitle: @"C:\Users\maxim\Pictures\capture.png",
                documentModifiedAtUtc: DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
                annotationCount: 2,
                properties: new Dictionary<string, string>
                {
                    ["sourceImagePath"] = @"C:\Users\maxim\Pictures\capture.png",
                    ["owner"] = "user@example.com"
                }),
            CancellationToken.None);

        string contents = await File.ReadAllTextAsync(journalPath);

        Assert.Contains("documentSaved", contents);
        Assert.Contains("document-1", contents);
        Assert.Contains("<path>", contents);
        Assert.Contains("<email>", contents);
        Assert.DoesNotContain(@"C:\Users\maxim\Pictures\capture.png", contents);
        Assert.DoesNotContain("user@example.com", contents);
    }

    [TestMethod]
    public async Task WriteAsync_AppendsMultipleEntries()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string journalPath = Path.Combine(workspace.Path, "logs", "recovery.jsonl");
        var journal = new FileCrashRecoveryJournal(journalPath);

        await journal.WriteAsync(
            RecoveryJournalEntry.Create(
                "session-1",
                RecoveryJournalEventKind.SessionStarted,
                "Started."),
            CancellationToken.None);
        await journal.WriteAsync(
            RecoveryJournalEntry.Create(
                "session-1",
                RecoveryJournalEventKind.SessionClosed,
                "Closed."),
            CancellationToken.None);

        string[] lines = await File.ReadAllLinesAsync(journalPath);

        Assert.AreEqual(2, lines.Length);
        Assert.Contains("sessionStarted", lines[0]);
        Assert.Contains("sessionClosed", lines[1]);
    }
}
