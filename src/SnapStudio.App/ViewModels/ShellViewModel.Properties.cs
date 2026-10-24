using Microsoft.UI.Xaml;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private AnnotationProperty CurrentAnnotationProperties => HasSelectedAnnotation
        ? AnnotationProperties.Common(_currentDocument!.Annotations.Where(item => SelectedAnnotationIds.Contains(item.Id)).Select(item => item.Kind))
        : IsSelectionToolActive ? AnnotationProperty.None : AnnotationProperties.For(ActiveAnnotationKind);

    private bool HasAnnotationProperty(AnnotationProperty property) => (CurrentAnnotationProperties & property) != 0;
    private Visibility PropertyVisibility(AnnotationProperty property) => HasAnnotationProperty(property) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AnnotationAppearanceControlsVisibility => PropertyVisibility(AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.TextSize);
    public Visibility AnnotationShapeControlsVisibility => PropertyVisibility(AnnotationProperty.StrokeWidth | AnnotationProperty.TextSize);
    public Visibility AnnotationColorControlsVisibility => PropertyVisibility(AnnotationProperty.Color);
    public Visibility AnnotationFillControlsVisibility => PropertyVisibility(AnnotationProperty.ShapeFill);
    public Visibility AnnotationOpacityControlsVisibility => PropertyVisibility(AnnotationProperty.Opacity | AnnotationProperty.BlurStrength);
    public Visibility AnnotationCornerStyleControlsVisibility => PropertyVisibility(AnnotationProperty.Corners);
    public Visibility AnnotationTextControlsVisibility => CanEditAnnotationText ? PropertyVisibility(AnnotationProperty.TextContent) : Visibility.Collapsed;
    public Visibility AnnotationSelectionControlsVisibility => IsSelectionToolActive && !HasSelectedAnnotation ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnnotationSelectionCropVisibility => IsSelectionToolActive && !HasSelectedAnnotation && CanCropToSelection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnnotationMultipleSelectionVisibility => SelectedAnnotationCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnnotationPresetControlsVisibility => !HasSelectedAnnotation && !IsSelectionToolActive ? Visibility.Visible : Visibility.Collapsed;
    public string AnnotationOpacityHeader => HasAnnotationProperty(AnnotationProperty.BlurStrength) ? "Blur strength" : "Opacity";
    public string AnnotationPrimaryPropertyHeader => HasAnnotationProperty(AnnotationProperty.StrokeWidth | AnnotationProperty.TextSize)
        ? AnnotationSizeHeader : AnnotationColorHeader;

    private string AnnotationColorHeader => SelectedAnnotationCount > 1 ? "Color" : ActiveOrSelectedAnnotationKind switch
    {
        AnnotationKind.Text => "Text color",
        AnnotationKind.Fill or AnnotationKind.Highlight => "Fill color",
        _ => "Stroke color"
    };

    public string AnnotationPropertiesTitle => SelectedAnnotationCount > 1 ? $"{SelectedAnnotationCount} objects selected"
        : GetSelectedAnnotation() is { } selected ? selected.Kind switch
        {
            AnnotationKind.Image => "Image", AnnotationKind.Fill => "Filled region",
            AnnotationKind.Highlight => "Highlighter", _ => selected.Kind.ToString()
        } : ActiveAnnotationToolLabel;

    public string AnnotationSelectionDescription => !HasCurrentDocument ? "Open or capture an image to start selecting objects."
        : SourceSelectionBounds is { } bounds
            ? $"Selected area: {Math.Round(bounds.Width)} × {Math.Round(bounds.Height)} px. Use Crop to keep this area."
            : "Drag a marquee to select objects. Shift adds to the selection; Ctrl toggles objects. Select an object to edit its properties.";

    public ColorRgba AnnotationPropertyColor => ResolveStyleColor();
    public bool HasMixedAnnotationColors => HasSelectedAnnotation && _currentDocument!.Annotations
        .Where(item => SelectedAnnotationIds.Contains(item.Id)).Select(item => item.Kind switch
        {
            AnnotationKind.Text => item.Style.Text,
            AnnotationKind.Highlight or AnnotationKind.Fill => item.Style.Fill,
            _ => item.Style.Stroke
        }).Distinct().Skip(1).Any();

    private bool CanApplyAnnotationProperty(SelectionStyleAspect aspect) => aspect switch
    {
        SelectionStyleAspect.Color => HasAnnotationProperty(AnnotationProperty.Color),
        SelectionStyleAspect.Width => HasAnnotationProperty(AnnotationProperty.StrokeWidth | AnnotationProperty.TextSize),
        SelectionStyleAspect.Corners => HasAnnotationProperty(AnnotationProperty.Corners),
        SelectionStyleAspect.Opacity => HasAnnotationProperty(AnnotationProperty.Opacity | AnnotationProperty.BlurStrength),
        SelectionStyleAspect.Font => HasAnnotationProperty(AnnotationProperty.Font),
        _ => CurrentAnnotationProperties != AnnotationProperty.None
    };

    private void NotifyPropertyContextChanged()
    {
        // The inspector changes as a unit. One notification also updates the compiled OneWay bindings.
        OnPropertyChanged(string.Empty);
    }
}
