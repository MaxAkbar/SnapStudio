using SnapStudio.Core.Settings;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveLoadAsync_RoundTripsSettings()
    {
        using var workspace = TemporaryWorkspace.Create();
        string settingsPath = Path.Combine(workspace.Path, "settings.json");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var store = new JsonSettingsStore(settingsPath, defaults);
        ApplicationSettings settings = defaults with
        {
            CaptureHotkey = "Ctrl+Shift+S",
            IncludeCursorByDefault = false,
            CopyCapturesToClipboard = true,
            FirstRunCompleted = true
        };

        await store.SaveAsync(settings, CancellationToken.None);
        ApplicationSettings loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("Ctrl+Shift+S", loaded.CaptureHotkey);
        Assert.False(loaded.IncludeCursorByDefault);
        Assert.True(loaded.CopyCapturesToClipboard);
        Assert.True(loaded.FirstRunCompleted);
        Assert.True(loaded.FeatureFlags["Capture.WgcStill"]);
    }

    [Fact]
    public async Task LoadAsync_MigratesUnversionedSettingsAndPersistsCurrentSchema()
    {
        using var workspace = TemporaryWorkspace.Create();
        string settingsPath = Path.Combine(workspace.Path, "settings.json");
        string defaultStorageRoot = Path.Combine(workspace.Path, "Documents");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(defaultStorageRoot);
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "storageRoot": "D:\\Captures",
              "captureHotkey": "Ctrl+Alt+S",
              "includeCursorByDefault": false,
              "copyCapturesToClipboard": true,
              "firstRunCompleted": true,
              "featureFlags": {
                "Editor.BlurTool": false,
                "Custom.Experimental": true
              }
            }
            """);
        var store = new JsonSettingsStore(settingsPath, defaults);

        ApplicationSettings loaded = await store.LoadAsync(CancellationToken.None);
        string migratedJson = await File.ReadAllTextAsync(settingsPath);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(@"D:\Captures", loaded.StorageRoot);
        Assert.Equal("Ctrl+Alt+S", loaded.CaptureHotkey);
        Assert.False(loaded.IncludeCursorByDefault);
        Assert.True(loaded.CopyCapturesToClipboard);
        Assert.True(loaded.FirstRunCompleted);
        Assert.False(loaded.FeatureFlags["Editor.BlurTool"]);
        Assert.True(loaded.FeatureFlags["Custom.Experimental"]);
        Assert.True(loaded.FeatureFlags["Capture.WgcStill"]);
        Assert.Contains(
            $"""
              "schemaVersion": {ApplicationSettings.CurrentSchemaVersion}
            """,
            migratedJson);
    }

    [Fact]
    public async Task LoadAsync_UsesDefaultsForMissingRequiredValues()
    {
        using var workspace = TemporaryWorkspace.Create();
        string settingsPath = Path.Combine(workspace.Path, "settings.json");
        string defaultStorageRoot = Path.Combine(workspace.Path, "Documents");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(defaultStorageRoot);
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "schemaVersion": 1,
              "storageRoot": "",
              "captureHotkey": "",
              "featureFlags": {}
            }
            """);
        var store = new JsonSettingsStore(settingsPath, defaults);

        ApplicationSettings loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(defaultStorageRoot, loaded.StorageRoot);
        Assert.Equal(defaults.CaptureHotkey, loaded.CaptureHotkey);
        Assert.True(loaded.FeatureFlags["Capture.WgcStill"]);
        Assert.False(loaded.FeatureFlags["V1.Ocr"]);
    }

    [Fact]
    public async Task SaveAsync_NormalizesOlderSettingsBeforeWriting()
    {
        using var workspace = TemporaryWorkspace.Create();
        string settingsPath = Path.Combine(workspace.Path, "settings.json");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var store = new JsonSettingsStore(settingsPath, defaults);
        ApplicationSettings olderSettings = defaults with
        {
            SchemaVersion = 1,
            FeatureFlags = new Dictionary<string, bool>
            {
                ["Capture.WgcStill"] = false,
                ["Custom.Flag"] = true
            }
        };

        await store.SaveAsync(olderSettings, CancellationToken.None);
        ApplicationSettings loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.False(loaded.FeatureFlags["Capture.WgcStill"]);
        Assert.True(loaded.FeatureFlags["Custom.Flag"]);
        Assert.True(loaded.FeatureFlags.ContainsKey("Editor.BlurTool"));
    }
}
