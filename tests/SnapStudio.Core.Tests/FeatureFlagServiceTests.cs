using SnapStudio.Core.Settings;

namespace SnapStudio.Core.Tests;

public sealed class FeatureFlagServiceTests
{
    [Fact]
    public void IsEnabled_ReturnsConfiguredValueAndDefaultsMissingFlagsToFalse()
    {
        var service = new InMemoryFeatureFlagService(new Dictionary<string, bool>
        {
            ["enabled"] = true,
            ["disabled"] = false
        });

        Assert.True(service.IsEnabled("enabled"));
        Assert.False(service.IsEnabled("disabled"));
        Assert.False(service.IsEnabled("missing"));
    }
}
