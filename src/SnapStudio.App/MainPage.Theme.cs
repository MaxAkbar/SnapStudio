using Microsoft.UI.Xaml;
using SnapStudio.Core.Settings;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private void ApplyAppTheme()
    {
        ElementTheme theme = ViewModel.ThemePreference switch
        {
            ApplicationThemePreference.Light => ElementTheme.Light,
            ApplicationThemePreference.System => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
        RequestedTheme = theme;
        // Include the window title bar and popup roots in the same preference.
        if (XamlRoot?.Content is FrameworkElement root) root.RequestedTheme = theme;
    }
}
