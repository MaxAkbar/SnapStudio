using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App.Controls;

public sealed record SvgDesignerGridSettings
{
    public SvgDesignerGridSettings(bool visible = false, double spacing = CanvasGridLayout.DefaultSpacing, bool snap = true, PerspectiveGridSettings? perspective = null, CanvasGridFamilies? families = null)
    {
        Visible = visible; Spacing = CanvasGridLayout.NormalizeSpacing(spacing); Snap = snap; Perspective = (perspective ?? new()).Normalize();
        Families = (families ?? new()).Normalize(Perspective.Density);
    }
    public bool Visible { get; }
    public double Spacing { get; }
    public bool Snap { get; }
    public PerspectiveGridSettings Perspective { get; }
    public CanvasGridFamilies Families { get; }
}

public sealed class SvgDesignerControl : Grid, IDisposable
{
    private readonly WebView2 _web = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _initialSvg;
    private readonly string[] _fonts;
    private readonly ElementTheme _theme;
    private readonly IReadOnlyList<ToolDesign> _strokePatterns;
    private readonly CustomLinePattern? _initialStroke;
    private readonly PerspectiveSnapTargets _initialTargets;
    private bool _started;
    public event EventHandler? Ready;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler<SvgDesignerGridSettings>? GridSettingsChanged;
    public SvgDesignerGridSettings GridSettings { get; private set; }

    public SvgDesignerControl(string svg, string[] fonts, ElementTheme theme, SvgDesignerGridSettings? grid = null,
        IReadOnlyList<ToolDesign>? strokePatterns = null, CustomLinePattern? initialStroke = null, PerspectiveSnapTargets initialTargets = PerspectiveSnapTargets.All)
    {
        _initialSvg = svg; _fonts = fonts; _theme = theme; GridSettings = grid ?? new();
        _strokePatterns = strokePatterns ?? []; _initialStroke = initialStroke;
        _initialTargets = initialTargets;
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(_web); SetRow(_error, 1); Children.Add(_error);
        Loaded += Initialize;
    }

    private async void Initialize(object sender, RoutedEventArgs args)
    {
        if (_started) return; _started = true;
        try
        {
            string appRoot = Environment.GetEnvironmentVariable("SNAPSTUDIO_DATA_ROOT") is { Length: > 0 } configured
                ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapStudio");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(appRoot, "SvgDesignerBrowser"), null);
            await _web.EnsureCoreWebView2Async(environment);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            const string editorUrl = "https://snapstudio-svg.invalid/index.html";
            core.NavigationStarting += (_, e) => { if (!e.Uri.Equals(editorUrl, StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) ShowError("The SVG editor page could not load: " + e.WebErrorStatus); };
            core.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using JsonDocument message = JsonDocument.Parse(e.WebMessageAsJson);
                    switch (message.RootElement.GetProperty("type").GetString())
                    {
                        case "ready":
                            core.PostWebMessageAsJson(JsonSerializer.Serialize(new
                            {
                                type = "load", svg = _initialSvg, theme = _theme.ToString(), fonts = _fonts,
                                strokePatterns = _strokePatterns.Select(pattern => new { id = pattern.Id, name = pattern.Name, svg = pattern.Svg }),
                                initialStroke = _initialStroke,
                                initialTargets = (int)_initialTargets,
                                grid = new
                                {
                                    visible = GridSettings.Visible, spacing = GridSettings.Spacing, snap = GridSettings.Snap,
                                    families = GridSettings.Families,
                                    mode = (int)GridSettings.Perspective.Mode, centerX = GridSettings.Perspective.CenterX,
                                    horizon = GridSettings.Perspective.Horizon, left = GridSettings.Perspective.Left, right = GridSettings.Perspective.Right,
                                    vertical = GridSettings.Perspective.Vertical, radius = GridSettings.Perspective.Radius, density = GridSettings.Perspective.Density
                                }
                            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                            break;
                        case "loaded": _ready.TrySetResult(); Ready?.Invoke(this, EventArgs.Empty); break;
                        case "import": ImportRequested?.Invoke(this, EventArgs.Empty); break;
                        case "export": ExportRequested?.Invoke(this, EventArgs.Empty); break;
                        case "save": SaveRequested?.Invoke(this, EventArgs.Empty); break;
                        case "gridChanged": ApplyGridSettings(message.RootElement); break;
                        case "error": ShowError(message.RootElement.GetProperty("message").GetString() ?? "The SVG could not be loaded."); break;
                    }
                }
                catch (JsonException) { ShowError("The SVG editor returned an invalid response."); }
            };
            string assets = Path.Combine(AppContext.BaseDirectory, "Assets", "SvgDesigner");
            if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("The SVG editor assets are missing.");
            core.SetVirtualHostNameToFolderMapping("snapstudio-svg.invalid", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate(editorUrl);
        }
        catch (Exception error) { _ready.TrySetException(error); ShowError("The SVG editor could not start: " + error.Message); }
    }

    private void ApplyGridSettings(JsonElement message)
    {
        if (!message.TryGetProperty("visible", out var visible) || visible.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !message.TryGetProperty("spacing", out var spacing) || spacing.ValueKind != JsonValueKind.Number
            || !spacing.TryGetDouble(out double value) || !double.IsFinite(value)
            || !message.TryGetProperty("snap", out var snap) || snap.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            ShowError("The SVG editor returned invalid grid settings.");
            return;
        }
        var previous = GridSettings.Perspective; bool valid = true;
        double Read(string name, double fallback)
        {
            if (!message.TryGetProperty(name, out var field)) return fallback;
            if (field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out double number) && double.IsFinite(number)) return number;
            valid = false; return fallback;
        }
        double mode = Read("mode", (int)previous.Mode), density = Read("density", previous.Density);
        var perspective = new PerspectiveGridSettings((CanvasGridMode)(int)Math.Clamp(mode, 0, 5), Read("centerX", previous.CenterX),
            Read("horizon", previous.Horizon), Read("left", previous.Left), Read("right", previous.Right), Read("vertical", previous.Vertical),
            Read("radius", previous.Radius), (int)Math.Clamp(Math.Round(density), 4, 32));
        bool hasFamilies = message.TryGetProperty("families", out var familyFields);
        if (hasFamilies && familyFields.ValueKind != JsonValueKind.Object) valid = false;
        GridGuideSettings ReadFamily(GridGuideFamily family)
        {
            var fallback = GridSettings.Families.Get(family);
            if (!hasFamilies || familyFields.ValueKind != JsonValueKind.Object || !familyFields.TryGetProperty(family.ToString().ToLowerInvariant(), out var field)) return fallback;
            if (field.ValueKind != JsonValueKind.Object) { valid = false; return fallback; }
            string color = fallback.Color;
            if (field.TryGetProperty("color", out var colorField))
            {
                if (colorField.ValueKind == JsonValueKind.String) color = colorField.GetString() ?? color;
                else valid = false;
            }
            double Number(string key, double previousValue)
            {
                if (!field.TryGetProperty(key, out var item)) return previousValue;
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number) && double.IsFinite(number)) return number;
                valid = false; return previousValue;
            }
            return new(color, Number("opacity", fallback.Opacity), (int)Math.Clamp(Math.Round(Number("density", fallback.Density)), 4, 32));
        }
        var families = new CanvasGridFamilies(ReadFamily(GridGuideFamily.Grid), ReadFamily(GridGuideFamily.Center), ReadFamily(GridGuideFamily.Left),
            ReadFamily(GridGuideFamily.Right), ReadFamily(GridGuideFamily.Top), ReadFamily(GridGuideFamily.Bottom));
        if (!valid || mode < 0 || mode > 5 || mode != Math.Truncate(mode)) { ShowError("The SVG editor returned invalid grid settings."); return; }
        var settings = new SvgDesignerGridSettings(visible.GetBoolean(), value, snap.GetBoolean(), perspective, families);
        if (GridSettings == settings) return;
        GridSettings = settings;
        GridSettingsChanged?.Invoke(this, settings);
    }

    public async Task<string> GetSvgAsync()
    {
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        string response = await _web.ExecuteScriptAsync("(()=>{try{return {svg:window.snapSvgDesigner.exportSvg()}}catch(e){return {error:e.message}}})()");
        using JsonDocument result = JsonDocument.Parse(response);
        if (result.RootElement.TryGetProperty("error", out var error)) throw new InvalidDataException(error.GetString());
        if (!result.RootElement.TryGetProperty("svg", out var svg)) throw new InvalidDataException("The SVG editor did not return artwork.");
        string normalized = SvgShape.Normalize(svg.GetString() ?? "");
        // Confirm the same renderer used by the capture canvas and exports can open the result.
        SnapStudio.Rendering.SvgRasterizer.Preview(normalized, 32, 32);
        return normalized;
    }

    public async Task LoadSvgAsync(string svg)
    {
        await _ready.Task;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "replace", svg = SvgShape.Normalize(svg) }));
    }

    public void ShowError(string message) { _error.Text = message; _error.Visibility = Visibility.Visible; }
    public void Dispose() => _web.Close();
}
