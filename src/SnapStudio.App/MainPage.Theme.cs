using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SnapStudio.Core.Settings;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private bool _quickThemeChanging;

    private void QuickThemeButton_Click(object sender, RoutedEventArgs args)
    {
        if (_quickThemeChanging || sender is not FrameworkElement anchor) return;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft };
        foreach (var theme in Enum.GetValues<ApplicationThemePreference>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = theme == ApplicationThemePreference.System ? "Use system setting" : theme.ToString(),
                GroupName = "WorkspaceTheme",
                IsChecked = ViewModel.ThemePreference == theme
            };
            item.Click += async (_, _) =>
            {
                if (_quickThemeChanging) return;
                _quickThemeChanging = true; QuickThemeButton.IsEnabled = false;
                try { await ViewModel.SetThemePreferenceAsync(theme, CancellationToken.None); }
                finally { _quickThemeChanging = false; QuickThemeButton.IsEnabled = true; }
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    private void ApplyAppTheme()
    {
        ElementTheme theme = ViewModel.ThemePreference switch
        {
            ApplicationThemePreference.Light => ElementTheme.Light,
            ApplicationThemePreference.System => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
        RequestedTheme = theme;
        _helpWindow?.SetTheme(theme);
        // Include the window title bar and popup roots in the same preference.
        if (XamlRoot?.Content is FrameworkElement root) root.RequestedTheme = theme;
    }
}
