using SnapStudio.Core.Settings;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class JsonSettingsStoreTests
{
    [TestMethod]
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
            FirstRunCompleted = true,
            Theme = ApplicationThemePreference.Light
        };

        await store.SaveAsync(settings, CancellationToken.None);
        ApplicationSettings loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual("Ctrl+Shift+S", loaded.CaptureHotkey);
        Assert.IsFalse(loaded.IncludeCursorByDefault);
        Assert.IsTrue(loaded.CopyCapturesToClipboard);
        Assert.IsTrue(loaded.FirstRunCompleted);
        Assert.AreEqual(ApplicationThemePreference.Light, loaded.Theme);
        Assert.AreEqual(ApplicationStorageBackend.FileSystem, loaded.StorageBackend);
        Assert.IsTrue(loaded.FeatureFlags["Capture.WgcStill"]);
    }

    [TestMethod]
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

        Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(@"D:\Captures", loaded.StorageRoot);
        Assert.AreEqual("Ctrl+Alt+S", loaded.CaptureHotkey);
        Assert.IsFalse(loaded.IncludeCursorByDefault);
        Assert.IsTrue(loaded.CopyCapturesToClipboard);
        Assert.IsTrue(loaded.FirstRunCompleted);
        Assert.AreEqual(ApplicationStorageBackend.FileSystem, loaded.StorageBackend);
        Assert.IsFalse(loaded.FeatureFlags["Editor.BlurTool"]);
        Assert.AreEqual(ApplicationThemePreference.Dark, loaded.Theme);
        Assert.IsTrue(loaded.FeatureFlags["Custom.Experimental"]);
        Assert.IsTrue(loaded.FeatureFlags["Capture.WgcStill"]);
        Assert.Contains(
            $"""
              "schemaVersion": {ApplicationSettings.CurrentSchemaVersion}
            """,
            migratedJson);
    }

    [TestMethod]
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

        Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(defaultStorageRoot, loaded.StorageRoot);
        Assert.AreEqual(defaults.CaptureHotkey, loaded.CaptureHotkey);
        Assert.AreEqual(ApplicationStorageBackend.FileSystem, loaded.StorageBackend);
        Assert.IsTrue(loaded.FeatureFlags["Capture.WgcStill"]);
        Assert.IsFalse(loaded.FeatureFlags["V1.Ocr"]);
    }

    [TestMethod]
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

        Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.IsFalse(loaded.FeatureFlags["Capture.WgcStill"]);
        Assert.AreEqual(ApplicationStorageBackend.FileSystem, loaded.StorageBackend);
        Assert.IsTrue(loaded.FeatureFlags["Custom.Flag"]);
        Assert.IsTrue(loaded.FeatureFlags.ContainsKey("Editor.BlurTool"));
    }

    [TestMethod]
    public async Task LoadAsync_PreservesDatabaseStorageBackend()
    {
        using var workspace = TemporaryWorkspace.Create();
        string settingsPath = Path.Combine(workspace.Path, "settings.json");
        string defaultStorageRoot = Path.Combine(workspace.Path, "Documents");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(defaultStorageRoot);
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "schemaVersion": 3,
              "storageRoot": "D:\\Captures",
              "storageBackend": "Database",
              "captureHotkey": "PrintScreen",
              "includeCursorByDefault": true,
              "copyCapturesToClipboard": false,
              "firstRunCompleted": true,
              "featureFlags": {}
            }
            """);
        var store = new JsonSettingsStore(settingsPath, defaults);

        ApplicationSettings loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(ApplicationStorageBackend.Database, loaded.StorageBackend);
        Assert.AreEqual(@"D:\Captures", loaded.StorageRoot);
    }

    [TestMethod]
    [DataRow(ApplicationThemePreference.Dark)]
    [DataRow(ApplicationThemePreference.Light)]
    [DataRow(ApplicationThemePreference.System)]
    public async Task ThemePreference_PersistsAcrossStoreInstances(ApplicationThemePreference theme)
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "settings.json");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(workspace.Path);
        await new JsonSettingsStore(path, defaults).SaveAsync(defaults with { Theme = theme }, CancellationToken.None);

        ApplicationSettings loaded = await new JsonSettingsStore(path, defaults).LoadAsync(CancellationToken.None);

        Assert.AreEqual(theme, loaded.Theme);
    }

    [TestMethod]
    public async Task LoadAsync_LegacySettingsKeepDarkThemeAndInvalidThemeUsesDefault()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path = Path.Combine(workspace.Path, "settings.json");
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(workspace.Path);
        var store = new JsonSettingsStore(path, defaults);
        await File.WriteAllTextAsync(path, """{"schemaVersion":3,"storageRoot":"D:\\Captures"}""");

        ApplicationSettings legacy = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(ApplicationThemePreference.Dark, legacy.Theme);
        Assert.AreEqual(@"D:\Captures", legacy.StorageRoot);
        Assert.Contains("\"theme\": \"Dark\"", await File.ReadAllTextAsync(path));
        await File.WriteAllTextAsync(path, """{"schemaVersion":4,"theme":99}""");
        Assert.AreEqual(ApplicationThemePreference.Dark, (await store.LoadAsync(CancellationToken.None)).Theme);
    }
}
