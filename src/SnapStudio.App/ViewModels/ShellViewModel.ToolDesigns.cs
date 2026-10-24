using SnapStudio.Core.Documents;
using SnapStudio.Storage;
using SnapStudio.Core.Primitives;
using Microsoft.UI.Xaml;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private IToolDesignStore? _toolDesignStore;
    private IToolDesignFilePicker? _toolDesignFilePicker;
    private IReadOnlyList<ToolDesign> _savedToolDesigns = [];
    private string? _toolDesignLoadFailure;
    private SnapStudio.Core.Primitives.ColorRgba _annotationDesignFill = SnapStudio.Core.Primitives.ColorRgba.Transparent;
    private readonly Dictionary<AnnotationKind, ToolDesign> _chosenToolDesigns = [];
    private readonly SemaphoreSlim _toolDesignGate = new(1, 1);
    public StrokePattern AnnotationPattern { get; private set; }
    public int AnnotationPatternIndex => (int)AnnotationPattern;
    public Visibility AnnotationPatternControlsVisibility => HasAnnotationProperty(AnnotationProperty.StrokeWidth) && ToolDesigns.SupportsPattern(ActiveOrSelectedAnnotationKind)
        && (HasSelectedAnnotation ? _currentDocument!.Annotations.Where(item => SelectedAnnotationIds.Contains(item.Id)).All(item => ToolDesigns.SupportsPattern(item.Kind)) : !IsSelectionToolActive)
            ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<ToolDesign> SavedToolDesigns => _savedToolDesigns;
    public ToolDesign? ActiveSvgToolDesign => _chosenToolDesigns.GetValueOrDefault(ActiveToolFamily) is { Svg: not null } chosen ? chosen
        : ActiveToolFamily == AnnotationKind.Vector ? ToolDesigns.BuiltIns.First(item => item.Kind == AnnotationKind.Vector) : null;
    public bool CanEditSelectedVector => SelectedAnnotationCount == 1 && GetSelectedAnnotation()?.Vector is not null;
    public Visibility SelectedVectorControlsVisibility => CanEditSelectedVector && !IsMarqueeToolActive ? Visibility.Visible : Visibility.Collapsed;
    public AnnotationVector? SelectedVector => CanEditSelectedVector ? GetSelectedAnnotation()?.Vector : null;
    public IEnumerable<ToolDesign> GetToolDesigns(AnnotationKind kind) => ToolDesigns.BuiltIns.Concat(_savedToolDesigns)
        .Where(item => item.Kind == kind).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);
    public StrokePattern GetToolPattern(AnnotationKind kind) => _chosenToolDesigns.GetValueOrDefault(kind)?.Style.Pattern ?? StrokePattern.Solid;
    public bool IsBuiltInDesign(Guid id) => ToolDesigns.BuiltIns.Any(item => item.Id == id);

    private async Task LoadToolDesignsAsync(CancellationToken token)
    {
        if (_toolDesignStore is null) return;
        try { _savedToolDesigns = await _toolDesignStore.LoadAsync(token).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { _toolDesignLoadFailure = exception.Message; ShowCaptureNotice("Tool designs unavailable", exception.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning); }
        OnPropertyChanged(nameof(SavedToolDesigns));
    }

    public void UseToolDesign(ToolDesign design)
    {
        ToolDesign validated = ToolDesigns.Validate(design);
        _chosenToolDesigns[validated.Kind] = validated;
        int index = ToolIndex(validated.Kind);
        UpdateAnnotationTool(index);
        StatusText = $"Tool design: {validated.Name}.";
    }

    public static int ToolIndex(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rectangle => 0, AnnotationKind.Ellipse => 1, AnnotationKind.Line => 2, AnnotationKind.Arrow => 3,
        AnnotationKind.Text => 4, AnnotationKind.Highlight => 5, AnnotationKind.Blur => 6, AnnotationKind.Pen => 7,
        AnnotationKind.Fill => 8, AnnotationKind.Vector => 9, _ => throw new ArgumentException("This object is not a drawing tool.")
    };

    public void UseDefaultToolDesign(AnnotationKind kind)
    {
        _chosenToolDesigns.Remove(kind); AnnotationStrokeIndex = 0;
        AnnotationStrokeThickness = kind == AnnotationKind.Text ? 24 : 2; AnnotationCornerRadius = 0;
        SetAnnotationFontFamily(AnnotationStyle.DefaultFontFamily); UpdateAnnotationTool(ToolIndex(kind));
    }

    private void ApplyRememberedToolDesign()
    {
        AnnotationPattern = StrokePattern.Solid;
        _annotationDesignFill = SnapStudio.Core.Primitives.ColorRgba.Transparent;
        if (!_chosenToolDesigns.TryGetValue(ActiveToolFamily, out ToolDesign? design)) return;
        AnnotationStyle style = design.Style;
        SelectAnnotationColor(design.Kind switch { AnnotationKind.Text => style.Text,
            AnnotationKind.Highlight or AnnotationKind.Fill => style.Fill, _ => style.Stroke });
        AnnotationStrokeThickness = style.StrokeThickness; AnnotationOpacity = style.Opacity;
        AnnotationCornerRadius = style.CornerRadius; AnnotationPattern = style.Pattern;
        _annotationDesignFill = style.Fill;
        SetAnnotationFontFamily(style.FontFamily);
    }

    public async Task UpdateAnnotationPatternAsync(int index, CancellationToken token)
    {
        if (!Enum.IsDefined((StrokePattern)index) || AnnotationPatternControlsVisibility != Visibility.Visible) return;
        MarkAnnotationPresetCustom();
        AnnotationPattern = (StrokePattern)index;
        if (SelectedAnnotationCount > 1)
            await EditSelectionAsync((editor, document, ids) => editor.SetStylesAsync(document,
                document.Annotations.Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id,
                    item => item.Style with { Pattern = AnnotationPattern }), token), "Stroke pattern updated.", token).ConfigureAwait(true);
        else if (GetSelectedAnnotation() is { } annotation && _annotationEditor is not null && _currentDocument is not null)
        {
            await _annotationEditor.SetStyleAsync(_currentDocument, annotation, annotation.Style with { Pattern = AnnotationPattern }, "Change stroke pattern", token).ConfigureAwait(true);
            await SaveCurrentDocumentAsync("Stroke pattern updated.", token).ConfigureAwait(true);
        }
        NotifyAnnotationStyleControlsChanged();
    }

    public async Task SaveToolDesignAsync(ToolDesign design, CancellationToken token)
    {
        EnsureToolDesignLibraryLoaded();
        if (_toolDesignStore is null) throw new InvalidOperationException("Tool design storage is unavailable.");
        await _toolDesignGate.WaitAsync(token).ConfigureAwait(true);
        try
        {
            ToolDesign validated = ToolDesigns.Validate(design);
            if (IsBuiltInDesign(validated.Id)) throw new InvalidOperationException("Save a copy of a built-in design.");
            var next = _savedToolDesigns.Where(item => item.Id != design.Id).Append(validated).ToArray();
            await _toolDesignStore.SaveAsync(next, token).ConfigureAwait(true);
            _savedToolDesigns = next;
            bool refreshDefault = false;
            foreach (AnnotationKind kind in _chosenToolDesigns.Where(pair => pair.Value.Id == validated.Id).Select(pair => pair.Key).ToArray())
            {
                if (kind == validated.Kind) { _chosenToolDesigns[kind] = validated; refreshDefault |= ActiveToolFamily == kind && !HasSelectedAnnotation; }
                else _chosenToolDesigns.Remove(kind);
            }
            if (refreshDefault) ApplyRememberedToolDesign();
            OnPropertyChanged(nameof(SavedToolDesigns));
            StatusText = "Tool design saved.";
        }
        finally { _toolDesignGate.Release(); }
    }

    public async Task RemoveToolDesignAsync(Guid id, CancellationToken token)
    {
        EnsureToolDesignLibraryLoaded();
        if (_toolDesignStore is null) return;
        await _toolDesignGate.WaitAsync(token).ConfigureAwait(true);
        try
        {
            var next = _savedToolDesigns.Where(item => item.Id != id).ToArray();
            await _toolDesignStore.SaveAsync(next, token).ConfigureAwait(true); _savedToolDesigns = next;
            foreach (var kind in _chosenToolDesigns.Where(pair => pair.Value.Id == id).Select(pair => pair.Key).ToArray()) _chosenToolDesigns.Remove(kind);
            OnPropertyChanged(nameof(SavedToolDesigns)); StatusText = "Tool design removed.";
        }
        finally { _toolDesignGate.Release(); }
    }

    public async Task ImportToolDesignsAsync(CancellationToken token)
    {
        EnsureToolDesignLibraryLoaded();
        if (_toolDesignFilePicker is null || _toolDesignStore is null) return;
        string? path = await _toolDesignFilePicker.PickImportAsync(token).ConfigureAwait(true);
        if (path is null) return;
        IReadOnlyList<ToolDesign> incoming = await JsonToolDesignStore.ReadPackAsync(path, token).ConfigureAwait(true);
        await _toolDesignGate.WaitAsync(token).ConfigureAwait(true);
        try
        {
            var next = _savedToolDesigns.ToList();
            foreach (ToolDesign source in incoming)
            {
                if (next.Any(item => item == source)) continue;
                ToolDesign design = source with { Id = next.Any(item => item.Id == source.Id) || IsBuiltInDesign(source.Id) ? Guid.NewGuid() : source.Id };
                string stem = design.Name.Length > 65 ? design.Name[..65] : design.Name;
                int suffix = 2;
                while (next.Any(item => item.Kind == design.Kind && item.Name.Equals(design.Name, StringComparison.OrdinalIgnoreCase)))
                    design = design with { Name = $"{stem} ({suffix++})" };
                next.Add(design);
            }
            await _toolDesignStore.SaveAsync(next, token).ConfigureAwait(true); _savedToolDesigns = next;
            OnPropertyChanged(nameof(SavedToolDesigns)); StatusText = "Tool designs imported.";
        }
        finally { _toolDesignGate.Release(); }
    }

    public async Task ExportToolDesignsAsync(CancellationToken token)
    {
        if (_toolDesignFilePicker is null) return;
        string? path = await _toolDesignFilePicker.PickExportAsync(token).ConfigureAwait(true);
        if (path is not null) await JsonToolDesignStore.WritePackAsync(path, _savedToolDesigns, token).ConfigureAwait(true);
        StatusText = path is null ? "Export cancelled." : "Tool designs exported.";
    }

    private void EnsureToolDesignLibraryLoaded()
    {
        if (_toolDesignLoadFailure is not null) throw new InvalidOperationException($"The existing design library could not be loaded and has been preserved. {_toolDesignLoadFailure}");
    }

    public async Task UpdateSelectedVectorAsync(string svg, CancellationToken token)
    {
        if (CurrentDocumentId is { } documentId && SelectedAnnotationId is { } id && CanEditSelectedVector)
            await UpdateVectorAsync(documentId, id, svg, token).ConfigureAwait(true);
    }

    public async Task UpdateVectorAsync(DocumentId documentId, Guid annotationId, string svg, CancellationToken token)
    {
        if (_currentDocument?.Id != documentId || _editCommandStack is null
            || _currentDocument.Annotations.FirstOrDefault(item => item.Id == annotationId) is not { Kind: AnnotationKind.Vector, Vector: not null } item)
            throw new InvalidOperationException("The SVG object is no longer open. Reopen it before editing.");
        var after = item.Vector with { Svg = SvgShape.Normalize(svg) };
        if (after == item.Vector) return;
        await _editCommandStack.ExecuteAsync(_currentDocument, new UpdateAnnotationVectorCommand(item.Id, item.Vector, after), token).ConfigureAwait(true);
        RefreshAnnotationOverlays(); NotifyEditorStateChanged();
        await SaveCurrentDocumentAsync("SVG object updated.", token).ConfigureAwait(true);
    }

    public async Task<(string Svg, string Name)?> ChooseSvgFileAsync(CancellationToken token)
    {
        string? path = _toolDesignFilePicker is null ? null : await _toolDesignFilePicker.PickSvgImportAsync(token).ConfigureAwait(true);
        if (path is null) return null;
        if (new FileInfo(path).Length > SvgShape.MaximumCharacters) throw new InvalidDataException("Choose an SVG smaller than 1 MB.");
        string svg = SvgShape.Normalize(await File.ReadAllTextAsync(path, token).ConfigureAwait(true));
        string name = Path.GetFileNameWithoutExtension(path);
        return (svg, name.Length > 80 ? name[..80] : name);
    }

    public async Task ExportSvgAsync(string svg, CancellationToken token)
    {
        string normalized = SvgShape.Normalize(svg);
        string? path = _toolDesignFilePicker is null ? null : await _toolDesignFilePicker.PickSvgExportAsync(token).ConfigureAwait(true);
        if (path is not null) await File.WriteAllTextAsync(path, normalized, token).ConfigureAwait(true);
    }
}
