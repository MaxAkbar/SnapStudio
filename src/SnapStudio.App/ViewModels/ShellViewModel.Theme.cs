using SnapStudio.Core.Settings;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private ApplicationThemePreference _themePreference = ApplicationThemePreference.Dark;
    private int _settingsThemeIndex;

    public ApplicationThemePreference ThemePreference
    {
        get => _themePreference;
        private set
        {
            if (_themePreference == value) return;
            _themePreference = value;
            OnPropertyChanged(nameof(ThemePreference));
        }
    }

    public int SettingsThemeIndex
    {
        get => _settingsThemeIndex;
        set
        {
            if (value < 0 || value > 2 || _settingsThemeIndex == value) return;
            _settingsThemeIndex = value;
            OnPropertyChanged(nameof(SettingsThemeIndex));
        }
    }

    private void InitializeTheme(ApplicationSettings settings)
    {
        ThemePreference = settings.Theme;
        SettingsThemeIndex = (int)settings.Theme;
    }
}
