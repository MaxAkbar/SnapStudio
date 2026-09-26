using System.Text.Json;
using SnapStudio.Core.Settings;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class JsonSettingsImportExportServiceTests
{
    [TestMethod]
    public async Task ExportAsync_WritesMigratedSettings()
    {
        using var workspace = TemporaryWorkspace.Create();
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var service = new JsonSettingsImportExportService(defaults);
        ApplicationSettings settings = defaults with
        {
            SchemaVersion = 1,
            CaptureHotkey = "Ctrl+Shift+S",
            StorageBackend = ApplicationStorageBackend.Database,
            FirstRunCompleted = true
        };
        string exportPath = Path.Combine(workspace.Path, "SnapStudio-settings.json");

        SettingsExportResult result = await service.ExportAsync(
            settings,
            exportPath,
            CancellationToken.None);
        string json = await File.ReadAllTextAsync(exportPath);
        ApplicationSettings? exported = JsonSerializer.Deserialize<ApplicationSettings>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, exported?.SchemaVersion);
        Assert.AreEqual("Ctrl+Shift+S", exported?.CaptureHotkey);
        Assert.AreEqual(ApplicationStorageBackend.Database, exported?.StorageBackend);
        Assert.Contains(
            """
              "storageBackend": "Database"
            """,
            json);
    }

    [TestMethod]
    public async Task ImportAsync_MigratesOlderSettingsAndPreservesCustomFlags()
    {
        using var workspace = TemporaryWorkspace.Create();
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var service = new JsonSettingsImportExportService(defaults);
        string importPath = Path.Combine(workspace.Path, "settings-import.json");
        await File.WriteAllTextAsync(
            importPath,
            """
            {
              "schemaVersion": 1,
              "storageRoot": "D:\\Captures",
              "captureHotkey": "Ctrl+Alt+S",
              "includeCursorByDefault": false,
              "copyCapturesToClipboard": true,
              "firstRunCompleted": true,
              "featureFlags": {
                "Capture.WgcStill": false,
                "Custom.Flag": true
              }
            }
            """);

        SettingsImportResult result = await service.ImportAsync(
            importPath,
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, result.Settings?.SchemaVersion);
        Assert.AreEqual(@"D:\Captures", result.Settings?.StorageRoot);
        Assert.AreEqual("Ctrl+Alt+S", result.Settings?.CaptureHotkey);
        Assert.IsFalse(result.Settings?.IncludeCursorByDefault);
        Assert.IsTrue(result.Settings?.CopyCapturesToClipboard);
        Assert.AreEqual(ApplicationStorageBackend.FileSystem, result.Settings?.StorageBackend);
        Assert.IsFalse(result.Settings?.FeatureFlags["Capture.WgcStill"]);
        Assert.IsTrue(result.Settings?.FeatureFlags["Custom.Flag"]);
        Assert.IsTrue(result.Settings?.FeatureFlags.ContainsKey("Editor.PdfExport"));
    }

    [TestMethod]
    public async Task ImportAsync_WhenJsonIsInvalid_ReturnsInvalidJsonFailure()
    {
        using var workspace = TemporaryWorkspace.Create();
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var service = new JsonSettingsImportExportService(defaults);
        string importPath = Path.Combine(workspace.Path, "settings-import.json");
        await File.WriteAllTextAsync(importPath, "{");

        SettingsImportResult result = await service.ImportAsync(
            importPath,
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(SettingsTransferFailureReason.InvalidJson, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task ExportAsync_WhenPathIsBlank_ReturnsInvalidPathFailure()
    {
        using var workspace = TemporaryWorkspace.Create();
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var service = new JsonSettingsImportExportService(defaults);

        SettingsExportResult result = await service.ExportAsync(
            defaults,
            "",
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(SettingsTransferFailureReason.InvalidPath, result.Failure?.Reason);
    }
}
