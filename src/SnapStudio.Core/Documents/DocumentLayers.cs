namespace SnapStudio.Core.Documents;

public static class DocumentLayers
{
    public static void EnsureInitialized(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Layers.Count == 0)
        {
            document.Layers.Add(new AnnotationLayer());
        }

        var layerIds = document.Layers.Select(layer => layer.Id).ToHashSet();
        Guid fallbackLayerId = document.Layers[0].Id;
        foreach (AnnotationObject annotation in document.Annotations)
        {
            if (annotation.LayerId is not Guid layerId || !layerIds.Contains(layerId))
            {
                annotation.LayerId = fallbackLayerId;
            }
        }

        document.SchemaVersion = CaptureDocument.CurrentSchemaVersion;
    }

    public static IEnumerable<AnnotationObject> InPaintOrder(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Layers.Count == 0)
        {
            return document.Annotations;
        }

        var layerOrder = document.Layers
            .Select((layer, index) => (layer.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        return document.Annotations
            .OrderBy(annotation => annotation.LayerId is Guid id && layerOrder.TryGetValue(id, out int index)
                ? index
                : 0);
    }

    public static bool IsEffectivelyVisible(CaptureDocument document, AnnotationObject annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        return annotation.IsVisible
            && (annotation.LayerId is not Guid layerId
                || document.Layers.FirstOrDefault(layer => layer.Id == layerId)?.IsVisible != false);
    }
}
