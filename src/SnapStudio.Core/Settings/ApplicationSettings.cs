using System.Text.Json.Serialization;

namespace SnapStudio.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ApplicationStorageBackend>))]
public enum ApplicationStorageBackend
{
    FileSystem,
    Database
}

public sealed record ApplicationSettings(
    int SchemaVersion,
    string StorageRoot,
    string CaptureHotkey,
    bool IncludeCursorByDefault,
    bool CopyCapturesToClipboard,
    bool FirstRunCompleted,
    ApplicationStorageBackend StorageBackend,
    Dictionary<string, bool> FeatureFlags)
{
    public const int CurrentSchemaVersion = 3;

    public ApplicationSettings()
        : this(
            CurrentSchemaVersion,
            string.Empty,
            "PrintScreen",
            true,
            false,
            false,
            ApplicationStorageBackend.FileSystem,
            [])
    {
    }

    public static ApplicationSettings CreateDefault(string storageRoot) => new(
        CurrentSchemaVersion,
        storageRoot,
        "PrintScreen",
        true,
        false,
        false,
        ApplicationStorageBackend.FileSystem,
        new Dictionary<string, bool>
        {
            ["Capture.WgcStill"] = true,
            ["Capture.GdiFallback"] = false,
            ["Capture.Delayed"] = true,
            ["Capture.IncludeCursor"] = true,
            ["Editor.BlurTool"] = true,
            ["Editor.PdfExport"] = true,
            ["Workspace.PinToScreen"] = true,
            ["V1.Ocr"] = false,
            ["V1.ScrollingCapture"] = false,
            ["V1.ScreenRecording"] = false,
            ["V2.SmartRedact"] = false,
            ["V2.PluginSdk"] = false
        });
}
