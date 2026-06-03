namespace SnapStudio.Core.Settings;

public interface ISettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}

public interface ISettingsImportExportService
{
    Task<SettingsExportResult> ExportAsync(
        ApplicationSettings settings,
        string destinationPath,
        CancellationToken cancellationToken);

    Task<SettingsImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken);
}

public interface ISettingsMigrator
{
    SettingsMigrationResult Migrate(ApplicationSettings settings, ApplicationSettings defaultSettings);
}

public interface IFeatureFlagService
{
    bool IsEnabled(string flagName);
}
