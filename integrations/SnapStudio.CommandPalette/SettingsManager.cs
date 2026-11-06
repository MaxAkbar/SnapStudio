using Microsoft.CommandPalette.Extensions.Toolkit;

namespace SnapStudio.CommandPalette;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private readonly TextSetting _applicationPath = new("SnapStudio.ApplicationPath", "SnapStudio application path (optional)",
        "Leave blank to use the last SnapStudio you opened. Otherwise enter the full path to SnapStudio.App.exe.", "");
    public string ApplicationPath => _applicationPath.Value ?? "";

    public SettingsManager()
    {
        string directory = Utilities.BaseSettingsPath("SnapStudio.CommandPalette");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
        Settings.Add(_applicationPath);
        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }
}
