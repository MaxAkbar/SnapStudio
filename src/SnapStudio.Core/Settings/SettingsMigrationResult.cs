namespace SnapStudio.Core.Settings;

public sealed record SettingsMigrationResult(ApplicationSettings Settings, bool WasChanged);
