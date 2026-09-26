using SnapStudio.Core.Diagnostics;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class FileDiagnosticLogTests
{
    [TestMethod]
    public async Task WriteAsync_AppendsRedactedJsonLine()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string logPath = Path.Combine(workspace.Path, "logs", "snapstudio.jsonl");
        var log = new FileDiagnosticLog(logPath);

        await log.WriteAsync(
            DiagnosticEvent.Create(
                DiagnosticSeverity.Warning,
                "SnapStudio.Tests",
                @"Could not open C:\Users\maxim\Pictures\capture.png for user@example.com from https://example.com/private/capture.",
                new Dictionary<string, string>
                {
                    ["storageRoot"] = @"C:\Users\maxim\AppData\Local\SnapStudio",
                    ["owner"] = "user@example.com",
                    ["targetUrl"] = "https://example.com/private/capture"
                }),
            CancellationToken.None);

        string contents = await File.ReadAllTextAsync(logPath);

        Assert.Contains("<path>", contents);
        Assert.Contains("<email>", contents);
        Assert.Contains("<url>", contents);
        Assert.DoesNotContain(@"C:\Users\maxim\Pictures\capture.png", contents);
        Assert.DoesNotContain(@"C:\Users\maxim\AppData\Local\SnapStudio", contents);
        Assert.DoesNotContain("user@example.com", contents);
        Assert.DoesNotContain("https://example.com/private/capture", contents);
    }
}
