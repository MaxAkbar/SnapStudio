using Microsoft.UI.Xaml;
using SnapStudio.App.Capture;
using SnapStudio.App.Composition;
using SnapStudio.Core.Diagnostics;
using SnapStudio.Platform.Windows;
using WinRT.Interop;

namespace SnapStudio.App;

public sealed partial class MainWindow : Window
{
    private readonly AppServices _services;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ThemeRoot.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        ThemeRoot.Loaded += (_, _) => UpdateTitleBarColors();

        App? app = Application.Current as App;
        _services = AppServices.CreateDefault(
            WindowNative.GetWindowHandle(this),
            new RegionSelectionOverlay(new WindowsScreenPreviewService()),
            app?.CrashRecoveryJournal,
            app?.RecoverySessionId,
            new CaptureTargetPicker(() => RootFrame.XamlRoot),
            new CaptureWindowController(this));
        Closed += MainWindow_Closed;
        RootFrame.Navigate(typeof(MainPage), _services);
    }

    private void UpdateTitleBarColors()
    {
        bool dark = ThemeRoot.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
        AppWindow.TitleBar.ButtonHoverForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonPressedForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 52, 55, 63) : Windows.UI.Color.FromArgb(255, 226, 231, 238);
        AppWindow.TitleBar.ButtonPressedBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 42, 45, 52) : Windows.UI.Color.FromArgb(255, 211, 220, 230);
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (RootFrame.Content is MainPage page) page.CloseHelp();

        await _services.CrashRecoveryJournal
            .WriteAsync(
                RecoveryJournalEntry.Create(
                    _services.RecoverySessionId,
                    RecoveryJournalEventKind.SessionClosed,
                    "Main window closed."),
                CancellationToken.None)
            .ConfigureAwait(false);
    }
}
