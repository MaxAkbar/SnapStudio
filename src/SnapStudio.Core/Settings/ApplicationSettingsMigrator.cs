namespace SnapStudio.Core.Settings;

public sealed class ApplicationSettingsMigrator : ISettingsMigrator
{
    public SettingsMigrationResult Migrate(
        ApplicationSettings settings,
        ApplicationSettings defaultSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaultSettings);

        string storageRoot = UseFallbackIfBlank(settings.StorageRoot, defaultSettings.StorageRoot);
        string captureHotkey = UseFallbackIfBlank(settings.CaptureHotkey, defaultSettings.CaptureHotkey);
        Dictionary<string, bool> featureFlags = MergeFeatureFlags(
            settings.FeatureFlags,
            defaultSettings.FeatureFlags);

        bool wasChanged = settings.SchemaVersion != ApplicationSettings.CurrentSchemaVersion
            || !string.Equals(settings.StorageRoot, storageRoot, StringComparison.Ordinal)
            || !string.Equals(settings.CaptureHotkey, captureHotkey, StringComparison.Ordinal)
            || !FeatureFlagsEqual(settings.FeatureFlags, featureFlags);

        ApplicationSettings migrated = settings with
        {
            SchemaVersion = ApplicationSettings.CurrentSchemaVersion,
            StorageRoot = storageRoot,
            CaptureHotkey = captureHotkey,
            FeatureFlags = featureFlags
        };

        return new SettingsMigrationResult(migrated, wasChanged);
    }

    private static string UseFallbackIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;
    }

    private static Dictionary<string, bool> MergeFeatureFlags(
        IReadOnlyDictionary<string, bool>? settingsFlags,
        IReadOnlyDictionary<string, bool>? defaultFlags)
    {
        var merged = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (defaultFlags is not null)
        {
            foreach (KeyValuePair<string, bool> flag in defaultFlags)
            {
                if (!string.IsNullOrWhiteSpace(flag.Key))
                {
                    merged[flag.Key] = flag.Value;
                }
            }
        }

        if (settingsFlags is not null)
        {
            foreach (KeyValuePair<string, bool> flag in settingsFlags)
            {
                if (!string.IsNullOrWhiteSpace(flag.Key))
                {
                    merged[flag.Key] = flag.Value;
                }
            }
        }

        return merged;
    }

    private static bool FeatureFlagsEqual(
        IReadOnlyDictionary<string, bool>? existingFlags,
        IReadOnlyDictionary<string, bool> migratedFlags)
    {
        if (existingFlags is null || existingFlags.Count != migratedFlags.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, bool> migratedFlag in migratedFlags)
        {
            if (!existingFlags.TryGetValue(migratedFlag.Key, out bool existingValue)
                || existingValue != migratedFlag.Value)
            {
                return false;
            }
        }

        return true;
    }
}
