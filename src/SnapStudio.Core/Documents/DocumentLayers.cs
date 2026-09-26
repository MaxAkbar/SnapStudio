namespace SnapStudio.Core.Documents;

public static class DocumentLayers
{
    public static void EnsureInitialized(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureSupportedVersion(document);

        if (document.Layers is null || document.Annotations is null)
        {
            throw new InvalidDataException("The document's layer or annotation collection is missing.");
        }

        if (document.Layers.Any(layer => layer is null)
            || document.Annotations.Any(annotation => annotation is null))
        {
            throw new InvalidDataException("The document contains a missing layer or annotation.");
        }

        if (document.SchemaVersion == 1)
        {
            // Version 1 did not assign annotations to layers. Preserve any layers
            // already present and place legacy annotations in the first one.
            EnsureDefaultLayer(document);
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
        else if (document.Layers.Count == 0 && document.Annotations.Count == 0)
        {
            // Earlier version 2 writes could persist an empty layer collection.
            EnsureDefaultLayer(document);
        }
        else if (document.Layers.Count <= 1
            && document.Annotations.Count > 0
            && document.Annotations.All(annotation => annotation.LayerId is null))
        {
            // Earlier version 2 writes could also persist a layerless document.
            // Only recognize the unambiguous legacy shape; mixed or orphaned
            // current-version references remain invalid.
            EnsureDefaultLayer(document);
            Guid layerId = document.Layers[0].Id;
            foreach (AnnotationObject annotation in document.Annotations)
            {
                annotation.LayerId = layerId;
            }
        }

        ValidateForSave(document);
    }

    public static void ValidateForSave(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureSupportedVersion(document);
        if (document.SchemaVersion != CaptureDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Document schema version {document.SchemaVersion} must be migrated before saving.");
        }

        if (document.Layers is null || document.Annotations is null || document.Layers.Count == 0)
        {
            throw new InvalidDataException("The document must have at least one layer and an annotation collection.");
        }

        var layerIds = new HashSet<Guid>();
        foreach (AnnotationLayer layer in document.Layers)
        {
            if (layer is null || layer.Id == Guid.Empty || !layerIds.Add(layer.Id))
            {
                throw new InvalidDataException("The document contains a missing, empty, or duplicate layer ID.");
            }
        }

        var annotationIds = new HashSet<Guid>();
        foreach (AnnotationObject annotation in document.Annotations)
        {
            if (annotation is null || annotation.Id == Guid.Empty || !annotationIds.Add(annotation.Id))
            {
                throw new InvalidDataException("The document contains a missing, empty, or duplicate annotation ID.");
            }

            if (annotation.LayerId is not Guid layerId || !layerIds.Contains(layerId))
            {
                throw new InvalidDataException($"Annotation {annotation.Id} does not belong to an existing layer.");
            }
        }
    }

    private static void EnsureDefaultLayer(CaptureDocument document)
    {
        if (document.Layers.Count == 0)
        {
            document.Layers.Add(new AnnotationLayer());
        }
    }

    private static void EnsureSupportedVersion(CaptureDocument document)
    {
        if (document.SchemaVersion > CaptureDocument.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Document schema version {document.SchemaVersion} is newer than this app supports ({CaptureDocument.CurrentSchemaVersion}).");
        }

        if (document.SchemaVersion < 1)
        {
            throw new InvalidDataException($"Document schema version {document.SchemaVersion} is invalid.");
        }
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
            && annotation.LayerId is Guid layerId
            && document.Layers.Any(layer => layer.Id == layerId && layer.IsVisible);
    }
}
