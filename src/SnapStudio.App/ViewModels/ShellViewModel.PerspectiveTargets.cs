using SnapStudio.Core.Documents;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private PerspectiveSnapTargets _defaultPerspectiveTargets = PerspectiveSnapTargets.All;
    public PerspectiveSnapTargets SvgEditorInitialTargets => GetSelectedAnnotation()?.Style.PerspectiveTargets ?? _defaultPerspectiveTargets;
    public IReadOnlyList<PerspectiveSnapTargets> PerspectiveTargetValues => HasSelectedAnnotation && _currentDocument is not null
        ? _currentDocument.Annotations.Where(item => SelectedAnnotationIds.Contains(item.Id)).Select(item => item.Style.PerspectiveTargets).ToArray()
        : [_defaultPerspectiveTargets];
    public string PerspectiveTargetsSummary
    {
        get
        {
            var values = PerspectiveTargetValues.Distinct().ToArray();
            string label = values.Length != 1 ? "Mixed" : values[0] == PerspectiveSnapTargets.All ? "All guides"
                : values[0] == PerspectiveSnapTargets.None ? "None" : string.Join(", ", CanvasGridFamilies.Active((CanvasGridMode)CanvasGridMode)
                    .Where(family => values[0].Allows(family)).Select(family => PerspectiveGuideEditing.Title(family, (CanvasGridMode)CanvasGridMode)));
            return $"Perspective targets: {(label.Length == 0 ? "No targets in this view" : label)}…";
        }
    }
    public async Task SetPerspectiveTargetsAsync(PerspectiveSnapTargets targets, bool? enabled, CancellationToken token)
    {
        PerspectiveTargetFilter.Validate(targets);
        PerspectiveSnapTargets Update(PerspectiveSnapTargets current) => enabled is null ? targets : enabled.Value ? current | targets : current & ~targets;
        if (!HasSelectedAnnotation)
        {
            _defaultPerspectiveTargets = Update(_defaultPerspectiveTargets);
            StatusText = "Perspective snap targets set for new drawings.";
        }
        else
            await EditSelectionAsync((editor, document, ids) => editor.SetStylesAsync(document,
                document.Annotations.Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id,
                    item => item.Style with { PerspectiveTargets = Update(item.Style.PerspectiveTargets) }), token), "Perspective snap targets updated.", token);
        OnPropertyChanged(string.Empty);
    }
    private PerspectiveSnapTargets SelectionPerspectiveTargets(Guid id)
    {
        var items = _currentDocument?.Annotations.Where(item => SelectedAnnotationCount > 1 ? SelectedAnnotationIds.Contains(item.Id) : item.Id == id).ToArray();
        return items is { Length: > 0 } ? items.Aggregate(PerspectiveSnapTargets.All, (targets, item) => targets & item.Style.PerspectiveTargets) : _defaultPerspectiveTargets;
    }
}
