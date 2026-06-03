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

        App? app = Application.Current as App;
        _services = AppServices.CreateDefault(
            WindowNative.GetWindowHandle(this),
            new RegionSelectionOverlay(new WindowsScreenPreviewService()),
            app?.CrashRecoveryJournal,
            app?.RecoverySessionId);
        Closed += MainWindow_Closed;
        RootFrame.Navigate(typeof(MainPage), _services);
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
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
