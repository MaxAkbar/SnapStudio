using SnapStudio.Core.Settings;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class FeatureFlagServiceTests
{
    [TestMethod]
    public void IsEnabled_ReturnsConfiguredValueAndDefaultsMissingFlagsToFalse()
    {
        var service = new InMemoryFeatureFlagService(new Dictionary<string, bool>
        {
            ["enabled"] = true,
            ["disabled"] = false
        });

        Assert.IsTrue(service.IsEnabled("enabled"));
        Assert.IsFalse(service.IsEnabled("disabled"));
        Assert.IsFalse(service.IsEnabled("missing"));
    }
}
