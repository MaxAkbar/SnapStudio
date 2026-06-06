using System.Text.Json;
using SnapStudio.Core.Settings;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

public sealed class JsonSettingsImportExportServiceTests
{
    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, exported?.SchemaVersion);
        Assert.Equal("Ctrl+Shift+S", exported?.CaptureHotkey);
        Assert.Equal(ApplicationStorageBackend.Database, exported?.StorageBackend);
        Assert.Contains(
            """
              "storageBackend": "Database"
            """,
            json);
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, result.Settings?.SchemaVersion);
        Assert.Equal(@"D:\Captures", result.Settings?.StorageRoot);
        Assert.Equal("Ctrl+Alt+S", result.Settings?.CaptureHotkey);
        Assert.False(result.Settings?.IncludeCursorByDefault);
        Assert.True(result.Settings?.CopyCapturesToClipboard);
        Assert.Equal(ApplicationStorageBackend.FileSystem, result.Settings?.StorageBackend);
        Assert.False(result.Settings?.FeatureFlags["Capture.WgcStill"]);
        Assert.True(result.Settings?.FeatureFlags["Custom.Flag"]);
        Assert.True(result.Settings?.FeatureFlags.ContainsKey("Editor.PdfExport"));
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(SettingsTransferFailureReason.InvalidJson, result.Failure?.Reason);
    }

    [Fact]
    public async Task ExportAsync_WhenPathIsBlank_ReturnsInvalidPathFailure()
    {
        using var workspace = TemporaryWorkspace.Create();
        ApplicationSettings defaults = ApplicationSettings.CreateDefault(Path.Combine(workspace.Path, "Documents"));
        var service = new JsonSettingsImportExportService(defaults);

        SettingsExportResult result = await service.ExportAsync(
            defaults,
            "",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SettingsTransferFailureReason.InvalidPath, result.Failure?.Reason);
    }
}
