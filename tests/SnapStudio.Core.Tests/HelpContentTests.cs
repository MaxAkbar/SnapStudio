using SnapStudio.App.Help;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class HelpContentTests
{
    [TestMethod]
    [DataRow(HelpContent.HomeUrl)]
    [DataRow(HelpContent.HomeUrl + "#layers")]
    [DataRow("https://SNAPSTUDIO-HELP.invalid:443/index.html#shortcuts")]
    public void IsHelpPage_AllowsBundledGuideAndTopicHistory(string address)
    {
        Assert.IsTrue(HelpContent.IsHelpPage(address));
    }

    [TestMethod]
    [DataRow("https://example.com/index.html")]
    [DataRow("https://snapstudio-help.invalid.example.com/index.html")]
    [DataRow("http://snapstudio-help.invalid/index.html")]
    [DataRow("https://snapstudio-help.invalid:8443/index.html")]
    [DataRow("https://user@snapstudio-help.invalid/index.html")]
    [DataRow("https://snapstudio-help.invalid/another.html")]
    [DataRow("https://snapstudio-help.invalid/index.html?redirect=example.com")]
    [DataRow("file:///C:/Windows/win.ini")]
    [DataRow("javascript:alert(1)")]
    [DataRow("data:text/html,hello")]
    [DataRow("about:blank")]
    [DataRow("/index.html")]
    [DataRow("")]
    public void IsHelpPage_BlocksExternalAndNonGuideNavigation(string address)
    {
        Assert.IsFalse(HelpContent.IsHelpPage(address));
    }
}
