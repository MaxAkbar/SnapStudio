using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using SnapStudio.App.Composition;
using Windows.Graphics;

namespace SnapStudio.App.Help;

internal sealed class HelpWindow : Window
{
    private readonly Grid _root = new();
    private readonly WebView2 _web = new();
    private readonly Button _back = CreateButton("Back", Symbol.Back);
    private readonly Button _forward = CreateButton("Forward", Symbol.Forward);
    private readonly Button _home = CreateButton("Help home", Symbol.Home);
    private readonly Button _retry = CreateButton("Retry loading help", Symbol.Refresh);
    private readonly ProgressRing _loading = new() { IsActive = true, Width = 32, Height = 32 };
    private readonly InfoBar _error = new()
    {
        Title = "Help is unavailable",
        Severity = InfoBarSeverity.Error,
        IsClosable = false,
        Margin = new Thickness(12)
    };
    private bool _initializing;
    private bool _closed;

    public HelpWindow(ElementTheme theme)
    {
        Title = "SnapStudio Help";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        _root.RequestedTheme = theme;
        _root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8, 12, 8),
            Children = { _back, _forward, _home, _retry }
        };
        _root.Children.Add(toolbar);
        Grid.SetRow(_error, 1);
        _root.Children.Add(_error);
        Grid.SetRow(_web, 2);
        _root.Children.Add(_web);
        Grid.SetRow(_loading, 2);
        _root.Children.Add(_loading);
        AutomationProperties.SetName(_web, "SnapStudio user guide");
        AutomationProperties.SetName(_loading, "Loading help");
        Content = _root;

        _back.IsEnabled = _forward.IsEnabled = _home.IsEnabled = false;
        _retry.Visibility = Visibility.Collapsed;
        _back.Click += (_, _) => { if (_web.CanGoBack) _web.GoBack(); };
        _forward.Click += (_, _) => { if (_web.CanGoForward) _web.GoForward(); };
        _home.Click += (_, _) => _web.CoreWebView2?.Navigate(HelpContent.HomeUrl);
        _retry.Click += async (_, _) => await LoadHelpAsync();
        _root.Loaded += async (_, _) =>
        {
            double scale = _root.XamlRoot.RasterizationScale;
            RectInt32 workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Resize(new SizeInt32(
                (int)Math.Min(1100 * scale, workArea.Width * 0.9),
                (int)Math.Min(800 * scale, workArea.Height * 0.9)));
            await LoadHelpAsync();
        };
        _root.ActualThemeChanged += (_, _) => ApplyBrowserTheme();
        Closed += (_, _) =>
        {
            _closed = true;
            _web.Close();
        };
    }

    public void SetTheme(ElementTheme theme) => _root.RequestedTheme = theme;

    private async Task LoadHelpAsync()
    {
        if (_initializing || _closed) return;
        _initializing = true;
        ApplyBrowserTheme();
        _web.Visibility = Visibility.Visible;
        _error.IsOpen = false;
        _retry.Visibility = Visibility.Collapsed;
        _loading.IsActive = true;
        _loading.Visibility = Visibility.Visible;
        try
        {
            string assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Help");
            foreach (string name in new[] { "index.html", "help.css", "help.js" })
            {
                if (!File.Exists(Path.Combine(assets, name)))
                    throw new FileNotFoundException("The bundled help files are missing. Extract or reinstall the complete SnapStudio package.");
            }

            if (_web.CoreWebView2 is null)
            {
                // Keep the browser cache in a writable user folder, including for MSIX installs.
                string browserData = Path.Combine(AppServices.ResolveAppDataRoot(), "HelpBrowser");
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, browserData, null);
                if (_closed) return;
                await _web.EnsureCoreWebView2Async(environment);
                if (_closed) return;

                CoreWebView2 core = _web.CoreWebView2
                    ?? throw new InvalidOperationException("The WebView2 Runtime could not initialize.");
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsWebMessageEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.NewWindowRequested += (_, args) => args.Handled = true;
                core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
                core.DownloadStarting += (_, args) => args.Cancel = true;
                core.NavigationStarting += (_, args) => args.Cancel = !HelpContent.IsHelpPage(args.Uri);
                core.HistoryChanged += (_, _) => UpdateNavigation();
                core.NavigationCompleted += (_, args) =>
                {
                    if (_closed) return;
                    StopLoading();
                    UpdateNavigation();
                    if (!args.IsSuccess) ShowError("The user guide could not load. Try again. (" + args.WebErrorStatus + ")");
                };
                core.ProcessFailed += (_, _) => ShowError("The help viewer stopped. Close Help and open it again.");
                core.SetVirtualHostNameToFolderMapping(HelpContent.HostName, assets, CoreWebView2HostResourceAccessKind.Deny);
            }

            ApplyBrowserTheme();
            (_web.CoreWebView2 ?? throw new InvalidOperationException("The help viewer is not ready."))
                .Navigate(HelpContent.HomeUrl);
            _home.IsEnabled = true;
        }
        catch (Exception error)
        {
            if (!_closed) ShowError("The offline guide could not open. " + error.Message);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void ApplyBrowserTheme()
    {
        if (_closed) return;
        bool dark = _root.ActualTheme == ElementTheme.Dark;
        _root.Background = new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(255, 24, 26, 31)
            : Windows.UI.Color.FromArgb(255, 247, 248, 250));
        if (_web.CoreWebView2 is null) return;
        _web.CoreWebView2.Profile.PreferredColorScheme = dark
            ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    private void UpdateNavigation()
    {
        _back.IsEnabled = _web.CanGoBack;
        _forward.IsEnabled = _web.CanGoForward;
    }

    private void StopLoading()
    {
        _loading.IsActive = false;
        _loading.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        if (_closed) return;
        StopLoading();
        _web.Visibility = Visibility.Collapsed;
        _back.IsEnabled = _forward.IsEnabled = _home.IsEnabled = false;
        _error.Message = message;
        _error.IsOpen = true;
        _retry.Visibility = Visibility.Visible;
    }

    private static Button CreateButton(string name, Symbol symbol)
    {
        var button = new Button { Content = new SymbolIcon(symbol) };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }
}
