using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Rendering;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly IStampStore? _stampStore;
    private readonly IOnlineStampCatalog? _onlineStampCatalog;
    private IReadOnlyList<StampDefinition> _savedStamps = [];
    private string? _stampLoadFailure;
    private StampDefinition? _activeStamp;
    private readonly SemaphoreSlim _stampGate = new(1, 1);
    private double _stampSize = 96;

    public IEnumerable<StampDefinition> AvailableStamps => StampCatalog.BuiltIns.Concat(_savedStamps)
        .OrderBy(stamp => stamp.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(stamp => stamp.Name, StringComparer.CurrentCultureIgnoreCase);
    public bool IsStampToolActive => _activeStamp is not null && !IsSelectionToolActive;
    public string ActiveStampName => _activeStamp?.Name ?? "Choose a stamp";
    public Visibility StampControlsVisibility => IsStampToolActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectedStampAttributionVisibility => SelectedVector?.Attribution is not null && !IsMarqueeToolActive ? Visibility.Visible : Visibility.Collapsed;
    public double StampSize
    {
        get => _stampSize;
        set { _stampSize = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 16, 1024) : 96; OnPropertyChanged(nameof(StampSize)); }
    }
    public bool IsBuiltInStamp(Guid id) => StampCatalog.BuiltIns.Any(stamp => stamp.Id == id);

    private async Task LoadStampsAsync(CancellationToken token)
    {
        if (_stampStore is null) return;
        try
        {
            _savedStamps = StampCatalog.ValidateLibrary(await _stampStore.LoadAsync(token));
            if (_savedStamps.Any(stamp => IsBuiltInStamp(stamp.Id))) throw new InvalidDataException("A custom stamp uses a built-in stamp ID.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _savedStamps = [];
            _stampLoadFailure = error.Message;
            ShowStampError("The saved library could not be loaded. " + error.Message);
        }
        OnPropertyChanged(nameof(AvailableStamps));
    }

    public void UseStamp(StampDefinition stamp)
    {
        StampDefinition validated = StampCatalog.Validate(stamp);
        UpdateAnnotationTool(9);
        _activeStamp = validated;
        NotifyAnnotationStyleControlsChanged();
        NotifyEditorStateChanged();
        StatusText = $"Stamp: {validated.Name}. Click the image to place copies; V moves and resizes them.";
    }

    public async Task<bool> ImportStampAsync(CancellationToken token)
    {
        EnsureStampLibraryWritable();
        if (await ChooseSvgFileAsync(token) is not { } file) return false;
        // Check the same renderer used by gallery previews and document export before saving the artwork.
        await Task.Run(() => SvgRasterizer.Preview(file.Svg, 64, 64), token);
        string name = string.IsNullOrWhiteSpace(file.Name) ? "Imported stamp" : file.Name;
        string candidate = name;
        for (int suffix = 2; AvailableStamps.Any(stamp => stamp.Category.Equals("Custom", StringComparison.OrdinalIgnoreCase)
            && stamp.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
            candidate = name[..Math.Min(name.Length, 70)] + $" ({suffix})";
        var imported = new StampDefinition(Guid.NewGuid(), candidate, "Custom", file.Svg);
        await SaveStampAsync(imported, token);
        UseStamp(imported);
        return true;
    }

    public async Task SaveStampAsync(StampDefinition stamp, CancellationToken token)
    {
        EnsureStampLibraryWritable();
        StampDefinition validated = StampCatalog.Validate(stamp);
        if (IsBuiltInStamp(validated.Id)) throw new InvalidOperationException("Save a copy to customize a built-in stamp.");
        await _stampGate.WaitAsync(token);
        try
        {
            var next = _savedStamps.Where(item => item.Id != validated.Id).Append(validated).ToArray();
            await _stampStore!.SaveAsync(next, token);
            _savedStamps = next;
            if (_activeStamp?.Id == validated.Id) _activeStamp = validated;
            OnPropertyChanged(nameof(AvailableStamps));
            NotifyAnnotationStyleControlsChanged();
            StatusText = "Stamp saved to your library.";
        }
        finally { _stampGate.Release(); }
    }

    public Task<OnlineStampSearchResult> SearchOnlineStampsAsync(string query, string? prefix, int start, CancellationToken token) =>
        (_onlineStampCatalog ?? throw new InvalidOperationException("Online stamp search is unavailable in this session.")).SearchAsync(query, prefix, start, token);

    public async Task<string> PreviewOnlineStampAsync(OnlineStampIcon icon, CancellationToken token)
    {
        string svg = await (_onlineStampCatalog ?? throw new InvalidOperationException("Online stamp search is unavailable in this session.")).DownloadAsync(icon, token);
        await Task.Run(() => SvgRasterizer.Preview(svg, 96, 96), token);
        return svg;
    }

    public async Task<StampDefinition> SaveOnlineStampAsync(OnlineStampIcon icon, string svg, CancellationToken token)
    {
        EnsureStampLibraryWritable();
        svg = SvgShape.Normalize(svg);
        if (StampCatalog.FindReusableOnlineStamp(_savedStamps, icon, svg) is { } existing)
            return existing;
        string name = icon.Name[..Math.Min(icon.Name.Length, 70)];
        string candidate = name;
        for (int suffix = 2; AvailableStamps.Any(stamp => stamp.Category.Equals(icon.Collection, StringComparison.OrdinalIgnoreCase)
            && stamp.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); suffix++) candidate = name + $" ({suffix})";
        var saved = new StampDefinition(Guid.NewGuid(), candidate, icon.Collection, svg) { Attribution = icon.Attribution };
        await SaveStampAsync(saved, token);
        return saved;
    }

    public async Task RemoveStampAsync(Guid id, CancellationToken token)
    {
        EnsureStampLibraryWritable();
        if (IsBuiltInStamp(id)) return;
        await _stampGate.WaitAsync(token);
        try
        {
            var next = _savedStamps.Where(stamp => stamp.Id != id).ToArray();
            await _stampStore!.SaveAsync(next, token);
            _savedStamps = next;
            if (_activeStamp?.Id == id) UseMoveTool();
            OnPropertyChanged(nameof(AvailableStamps));
            StatusText = "Stamp removed from the library. Placed copies are kept.";
        }
        finally { _stampGate.Release(); }
    }

    private void EnsureStampLibraryWritable()
    {
        if (_stampLoadFailure is not null) throw new InvalidOperationException("The saved stamp library could not be loaded; it has been kept unchanged. " + _stampLoadFailure);
        if (_stampStore is null) throw new InvalidOperationException("The stamp library is unavailable in this session.");
    }

    public void ShowStampError(string message)
    {
        StatusText = "Stamps: " + message;
        ShowCaptureNotice("Stamps", message, InfoBarSeverity.Warning);
    }

    public async Task PlaceStampAtCanvasPointAsync(PointD displayPoint, CancellationToken token, bool bypassPerspectiveSnapping = false)
    {
        if (!IsStampToolActive || _activeStamp is null || _currentDocument is not { } document
            || _annotationEditor is null || _documentRepository is null || !_canvasViewport.HasSource) return;
        StampDefinition selected = _activeStamp;
        AnnotationObject stamp = StampCatalog.Place(selected, _canvasViewport.ToSourcePoint(displayPoint),
            new(document.SourceImage.Width, document.SourceImage.Height), StampSize);
        SnapNewObjectPlacement(stamp, bypassPerspectiveSnapping);
        if (!_layerWorkspace.TryAssignActiveLayer(document, stamp)) { StatusText = "Show the selected layer before placing stamps."; return; }
        await _annotationEditor.AddAsync(document, stamp, token);
        _annotationSelection.Clear();
        await SaveCurrentDocumentAsync($"{selected.Name} stamp placed. Click again for another copy; V moves objects.", token);
    }
}
