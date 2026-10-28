using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private CustomLinePattern? _annotationCustomPattern;
    private readonly Dictionary<AnnotationKind, CustomLinePattern> _customStrokeDefaults = [];
    public IReadOnlyList<ToolDesign> SavedStrokePatterns => _savedToolDesigns.Where(d => d.IsStrokePattern).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public CustomLinePattern? CurrentCustomStrokePattern => _annotationCustomPattern;
    public CustomLinePattern? SelectedCustomStrokePattern => GetSelectedAnnotation()?.Style is { Pattern: StrokePattern.Custom } style ? style.CustomPattern : null;
    public Visibility CustomStrokeSettingsVisibility => AnnotationPatternControlsVisibility == Visibility.Visible && AnnotationPattern == StrokePattern.Custom ? Visibility.Visible : Visibility.Collapsed;
    public string CustomStrokeName => _annotationCustomPattern?.Name ?? "Choose an SVG pattern";
    public double CustomStrokeScalePercent => (_annotationCustomPattern?.Scale ?? 1) * 100;
    public double CustomStrokeSpacingPercent => (_annotationCustomPattern?.Spacing ?? 1) * 100;

    public Task UseStrokePatternAsync(ToolDesign pattern, CancellationToken token) =>
        SetCustomStrokeAsync(new(pattern.Svg ?? throw new InvalidDataException("The pattern has no SVG."), pattern.Name), token);

    public async Task SetCustomStrokeAsync(CustomLinePattern pattern, CancellationToken token)
    {
        pattern = CustomLinePattern.Normalize(pattern);
        foreach (AnnotationStyle style in HasSelectedAnnotation ? _currentDocument!.Annotations.Where(item => SelectedAnnotationIds.Contains(item.Id)).Select(item => item.Style) : [AnnotationStyle.Default with { StrokeThickness = AnnotationStrokeThickness }])
            CustomLinePattern.ValidateStyle(style with { Pattern = StrokePattern.Custom, CustomPattern = pattern });
        _annotationCustomPattern = pattern; AnnotationPattern = StrokePattern.Custom; MarkAnnotationPresetCustom();
        if (!HasSelectedAnnotation) _customStrokeDefaults[ActiveToolFamily] = pattern;
        if (SelectedAnnotationCount > 1)
            await EditSelectionAsync((editor, document, ids) => editor.SetStylesAsync(document,
                document.Annotations.Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id,
                    item => item.Style with { Pattern = StrokePattern.Custom, CustomPattern = pattern }), token), "Custom line pattern updated.", token);
        else if (GetSelectedAnnotation() is { } annotation && _annotationEditor is not null && _currentDocument is not null)
        {
            await _annotationEditor.SetStyleAsync(_currentDocument, annotation, annotation.Style with { Pattern = StrokePattern.Custom, CustomPattern = pattern }, "Change custom line pattern", token);
            await SaveCurrentDocumentAsync("Custom line pattern updated.", token);
        }
        else StatusText = $"Custom line: {pattern.Name}.";
        NotifyAnnotationStyleControlsChanged();
    }
}
