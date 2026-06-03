using System.Text.Json;
using SnapStudio.Core.Settings;

namespace SnapStudio.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ApplicationSettings _defaultSettings;
    private readonly string _settingsPath;
    private readonly ISettingsMigrator _settingsMigrator;

    public JsonSettingsStore(
        string settingsPath,
        ApplicationSettings defaultSettings,
        ISettingsMigrator? settingsMigrator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(defaultSettings);

        _settingsPath = Path.GetFullPath(settingsPath);
        _defaultSettings = defaultSettings;
        _settingsMigrator = settingsMigrator ?? new ApplicationSettingsMigrator();
    }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return _settingsMigrator
                .Migrate(_defaultSettings, _defaultSettings)
                .Settings;
        }

        ApplicationSettings? settings;
        await using (FileStream stream = File.OpenRead(_settingsPath))
        {
            settings = await JsonSerializer
                .DeserializeAsync<ApplicationSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        SettingsMigrationResult migration = _settingsMigrator.Migrate(
            settings ?? _defaultSettings,
            _defaultSettings);
        if (migration.WasChanged)
        {
            await SaveAsync(migration.Settings, cancellationToken).ConfigureAwait(false);
        }

        return migration.Settings;
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{_settingsPath}.tmp";
        ApplicationSettings migratedSettings = _settingsMigrator
            .Migrate(settings, _defaultSettings)
            .Settings;
        string json = JsonSerializer.Serialize(migratedSettings, JsonOptions);

        await File
            .WriteAllTextAsync(temporaryPath, json, cancellationToken)
            .ConfigureAwait(false);

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
