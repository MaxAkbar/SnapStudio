namespace SnapStudio.Core.Settings;

public sealed class InMemoryFeatureFlagService : IFeatureFlagService
{
    private readonly IReadOnlyDictionary<string, bool> _flags;

    public InMemoryFeatureFlagService(IReadOnlyDictionary<string, bool> flags)
    {
        _flags = flags;
    }

    public bool IsEnabled(string flagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagName);

        return _flags.TryGetValue(flagName, out bool enabled) && enabled;
    }
}
