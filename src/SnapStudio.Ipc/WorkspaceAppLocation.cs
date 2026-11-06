using System.Text.Json;

namespace SnapStudio.Ipc;

/// <summary>Best-effort discovery of the last editor the user opened. No PowerToys detection.</summary>
public static class WorkspaceAppLocation
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetEnvironmentVariable("SNAPSTUDIO_DATA_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapStudio"),
        "workspace-app.json");

    public static bool TryRemember(string executablePath, string? locationFile = null)
    {
        string file = locationFile ?? DefaultFilePath;
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (ValidateExecutable(executablePath) is not string validated) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Location(1, validated), ExternalCaptureServer.JsonOptions));
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception error) when (IsLocationError(error)) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (IsLocationError(error)) { }
        }
    }

    public static string? Resolve(string? configuredPath = null, string? locationFile = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(configuredPath)) return ValidateExecutable(configuredPath.Trim().Trim('"'));
            string file = locationFile ?? DefaultFilePath;
            if (!File.Exists(file) || new FileInfo(file).Length > 8192) return null;
            var location = JsonSerializer.Deserialize<Location>(File.ReadAllText(file), ExternalCaptureServer.JsonOptions);
            return location is { ProtocolVersion: 1 } ? ValidateExecutable(location.ExecutablePath) : null;
        }
        catch (Exception error) when (IsLocationError(error)) { return null; }
    }

    private static string? ValidateExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetFileName(path), "SnapStudio.App.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path)) return null;
        string manifest = Path.Combine(Path.GetDirectoryName(path)!, "SnapStudio.integration.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 8192) return null;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        return json.RootElement.TryGetProperty("workspaceCommandProtocol", out var version)
            && version.TryGetInt32(out int protocol) && protocol == 1 ? Path.GetFullPath(path) : null;
    }

    private static bool IsLocationError(Exception error) => error is IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or NotSupportedException or InvalidOperationException;

    private sealed record Location(int ProtocolVersion, string ExecutablePath);
}
