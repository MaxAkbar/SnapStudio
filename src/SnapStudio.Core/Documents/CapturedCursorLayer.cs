using SnapStudio.Core.Capture;

namespace SnapStudio.Core.Documents;

public static class CapturedCursorLayer
{
    public static void AddTo(CaptureDocument document, CapturedCursor? cursor)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (cursor is null) return;

        var layer = new AnnotationLayer { Name = "Mouse pointer" };
        document.Layers.Add(layer);
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Image,
            LayerId = layer.Id,
            Text = "Mouse pointer",
            Bounds = cursor.Bounds,
            Image = cursor.Image.DeepCopy()
        });
    }
}
