using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;
using SnapStudio.Rendering;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private string _annotationFontFamily = AnnotationStyle.DefaultFontFamily;

    public IReadOnlyList<string> AnnotationFontFamilies => SystemFontCatalog.Families;

    public string AnnotationFontFamily => SystemFontCatalog.ResolveFamily(_annotationFontFamily);

    public Visibility AnnotationFontControlsVisibility => HasSelectedAnnotation
        ? _currentDocument!.Annotations.Any(item => SelectedAnnotationIds.Contains(item.Id) && item.Kind == AnnotationKind.Text)
            ? Visibility.Visible : Visibility.Collapsed
        : ActiveAnnotationKind == AnnotationKind.Text ? Visibility.Visible : Visibility.Collapsed;

    private void SetAnnotationFontFamily(string? family)
    {
        string resolved = SystemFontCatalog.ResolveFamily(family);
        if (_annotationFontFamily == resolved) return;
        _annotationFontFamily = resolved;
        OnPropertyChanged(nameof(AnnotationFontFamily));
    }

    public async Task UpdateAnnotationFontFamilyAsync(string family, CancellationToken cancellationToken)
    {
        if (!AnnotationFontFamilies.Contains(family, StringComparer.OrdinalIgnoreCase)) return;
        SetAnnotationFontFamily(family);
        MarkAnnotationPresetCustom();
        await ApplySelectedAnnotationStyleAsync("Change Font", cancellationToken, SelectionStyleAspect.Font).ConfigureAwait(true);
    }

    private void SyncAnnotationFontFromSelection()
    {
        AnnotationObject? text = _currentDocument?.Annotations.FirstOrDefault(item => SelectedAnnotationIds.Contains(item.Id) && item.Kind == AnnotationKind.Text);
        if (text is not null) SetAnnotationFontFamily(text.Style.FontFamily);
    }
}
