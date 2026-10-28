using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using SnapStudio.App.ViewModels.Items;
using SnapStudio.Core.Documents;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private static void ApplyLayerClip(FrameworkElement element, AnnotationOverlayItem item)
    {
        if (item.LayerClipRectangles is not { } regions || element is not Grid root) return;
        var content = new Grid { Width = item.Width, Height = item.Height, Tag = "LayerContent", DataContext = item };
        foreach (UIElement child in root.Children.ToArray())
        {
            if (child is FrameworkElement { Tag: string tag } && Enum.TryParse<AnnotationBoundsHandle>(tag, out _)) continue;
            root.Children.Remove(child); content.Children.Add(child);
        }
        root.Children.Insert(0, content);
        var visual = ElementCompositionPreview.GetElementVisual(content);
        var device = CanvasDevice.GetSharedDevice();
        var geometries = regions.Select(r => CanvasGeometry.CreateRectangle(device, (float)r.X, (float)r.Y, (float)r.Width, (float)r.Height)).ToArray();
        try
        {
            using var geometry = CanvasGeometry.CreateGroup(device, geometries, CanvasFilledRegionDetermination.Winding);
            var path = visual.Compositor.CreatePathGeometry(); path.Path = new CompositionPath(geometry);
            visual.Clip = visual.Compositor.CreateGeometricClip(path);
        }
        finally { foreach (var geometry in geometries) geometry.Dispose(); }
    }
}
