using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SnapStudio.App.ViewModels.Items;
using Windows.Foundation;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private sealed record AnnotationVisual(FrameworkElement Element, int Version, int SelectionCount);
    private readonly Dictionary<Guid, AnnotationVisual> _annotationVisuals = [];
    private object? _annotationVisualDocument;
    private object? _annotationVisualSource;

    private void SynchronizeAnnotationCanvas()
    {
        // Switching documents or replacing raster pixels invalidates blur/image content too.
        if (!Equals(_annotationVisualDocument, ViewModel.CurrentDocumentId)
            || !ReferenceEquals(_annotationVisualSource, ViewModel.CanvasImageSource))
        {
            AnnotationCanvas.Children.Clear();
            _annotationVisuals.Clear();
            _annotationVisualDocument = ViewModel.CurrentDocumentId;
            _annotationVisualSource = ViewModel.CanvasImageSource;
        }
        var retained = ViewModel.AnnotationOverlays.Select(item => item.Id).ToHashSet();
        foreach (Guid id in _annotationVisuals.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            AnnotationCanvas.Children.Remove(_annotationVisuals[id].Element);
            _annotationVisuals.Remove(id);
        }
        int order = 0;
        foreach (AnnotationOverlayItem item in ViewModel.AnnotationOverlays)
        {
            int selectionCount = item.IsSelected ? ViewModel.SelectedAnnotationCount : 0;
            if (!_annotationVisuals.TryGetValue(item.Id, out AnnotationVisual? visual)
                || visual.Version != item.VisualVersion || visual.SelectionCount != selectionCount)
            {
                if (visual is not null) AnnotationCanvas.Children.Remove(visual.Element);
                FrameworkElement element = CreateAnnotationElement(item);
                visual = new AnnotationVisual(element, item.VisualVersion, selectionCount);
                _annotationVisuals[item.Id] = visual;
                AnnotationCanvas.Children.Add(element);
            }
            FrameworkElement target = visual.Element;
            target.Visibility = _annotationDragBounds.ContainsKey(item.Id) ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetLeft(target, item.Left);
            Canvas.SetTop(target, item.Top);
            Canvas.SetZIndex(target, order++);
            if (target is Panel panel)
            {
                var clip = new Rect(-item.Left, -item.Top, ViewModel.CanvasDisplayWidth, ViewModel.CanvasDisplayHeight);
                foreach (Canvas drawing in panel.Children.OfType<Canvas>())
                    if (drawing.Clip is RectangleGeometry geometry && geometry.Rect != clip) geometry.Rect = clip;
            }
        }
    }
}
