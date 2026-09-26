using SnapStudio.Core.Diagnostics;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DefaultSensitiveDataRedactorTests
{
    [TestMethod]
    public void Redact_ReplacesLocalPathsEmailAddressesAndUrls()
    {
        var redactor = new DefaultSensitiveDataRedactor();

        string redacted = redactor.Redact(
            @"Failed to open C:\Users\maxim\Pictures\capture.png for user@example.com at https://example.com/private/capture?id=42.");

        Assert.Contains("<path>", redacted);
        Assert.Contains("<email>", redacted);
        Assert.Contains("<url>", redacted);
        Assert.DoesNotContain(@"C:\Users\maxim\Pictures\capture.png", redacted);
        Assert.DoesNotContain("user@example.com", redacted);
        Assert.DoesNotContain("https://example.com/private/capture", redacted);
    }

    [TestMethod]
    public void RedactProperties_RedactsValuesButKeepsKeys()
    {
        var redactor = new DefaultSensitiveDataRedactor();

        IReadOnlyDictionary<string, string> redacted = redactor.RedactProperties(
            new Dictionary<string, string>
            {
                ["storageRoot"] = @"C:\Users\maxim\AppData\Local\SnapStudio",
                ["owner"] = "owner@example.com",
                ["supportUrl"] = "https://example.com/users/owner@example.com/ticket"
            });

        Assert.AreEqual("<path>", redacted["storageRoot"]);
        Assert.AreEqual("<email>", redacted["owner"]);
        Assert.AreEqual("<url>", redacted["supportUrl"]);
    }
}
