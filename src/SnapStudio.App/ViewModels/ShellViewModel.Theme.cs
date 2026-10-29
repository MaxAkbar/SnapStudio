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
            OnPropertyChanged(nameof(QuickThemeHelpText));
            OnPropertyChanged(nameof(QuickThemeIcon));
        }
    }

    public string QuickThemeIcon => ThemePreference switch
    {
        ApplicationThemePreference.Light => "ThemeLight",
        ApplicationThemePreference.System => "ThemeSystem",
        _ => "ThemeDark"
    };

    public string QuickThemeHelpText => $"Theme: {ThemePreference}. Choose Dark, Light, or Use system setting. Changes apply and save immediately.";

    public async Task SetThemePreferenceAsync(ApplicationThemePreference theme, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (_settingsStore is null) return;

        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(true);
            if (settings.Theme != theme)
                await _settingsStore.SaveAsync(settings with { Theme = theme }, cancellationToken).ConfigureAwait(true);

            ThemePreference = theme;
            SettingsThemeIndex = (int)theme;
            StatusText = $"Theme: {theme}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            StatusText = "Theme could not be saved.";
            ShowCaptureNotice("Theme not changed", error.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
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
