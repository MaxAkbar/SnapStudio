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
        else if (document.SchemaVersion == 2 && document.Layers.Count == 0 && document.Annotations.Count == 0)
        {
            // Earlier version 2 writes could persist an empty layer collection.
            EnsureDefaultLayer(document);
        }
        else if (document.SchemaVersion == 2 && document.Layers.Count <= 1
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

        if (document.SchemaVersion is 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11)
        {
            document.SchemaVersion = CaptureDocument.CurrentSchemaVersion;
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
            LayerClipping.Validate(layer, document.SourceImage.Width, document.SourceImage.Height);
        }

        var annotationIds = new HashSet<Guid>();
        foreach (AnnotationObject annotation in document.Annotations)
        {
            if (annotation is null || annotation.Id == Guid.Empty || !annotationIds.Add(annotation.Id))
            {
                throw new InvalidDataException("The document contains a missing, empty, or duplicate annotation ID.");
            }

            if (annotation.Style is null || !Enum.IsDefined(annotation.Style.Pattern))
                throw new InvalidDataException("The annotation stroke pattern is invalid.");
            CustomLinePattern.ValidateStyle(annotation.Style);
            PerspectiveTargetFilter.Validate(annotation.Style.PerspectiveTargets);

            if (annotation.LayerId is not Guid layerId || !layerIds.Contains(layerId))
            {
                throw new InvalidDataException($"Annotation {annotation.Id} does not belong to an existing layer.");
            }

            if (annotation.Kind == AnnotationKind.Pen)
            {
                FreehandStroke.Validate(annotation);
            }
            else if (annotation.Kind == AnnotationKind.Fill)
            {
                AreaFill.Validate(annotation);
            }
            else if (annotation.Kind == AnnotationKind.Vector)
            {
                if (annotation.Vector is null || !double.IsFinite(annotation.Bounds.X) || !double.IsFinite(annotation.Bounds.Y)
                    || !double.IsFinite(annotation.Bounds.Width) || !double.IsFinite(annotation.Bounds.Height) || annotation.Bounds.Width <= 0 || annotation.Bounds.Height <= 0)
                    throw new InvalidDataException("A vector object must contain SVG artwork and valid bounds.");
                SvgShape.Normalize(annotation.Vector.Svg);
            }
            else if (annotation.Kind == AnnotationKind.Image)
            {
                if (annotation.Image is not { Width: > 0, Height: > 0, PngBytes.Length: > 0 }
                    || !double.IsFinite(annotation.Bounds.X) || !double.IsFinite(annotation.Bounds.Y)
                    || !double.IsFinite(annotation.Bounds.Width) || !double.IsFinite(annotation.Bounds.Height)
                    || annotation.Bounds.Width <= 0 || annotation.Bounds.Height <= 0)
                    throw new InvalidDataException("An image object must contain PNG data and valid bounds.");
            }
        }
        LayerStructure.Validate(document);
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

    public static int FindAdjacentAnnotationIndex(CaptureDocument document, Guid annotationId, int direction)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Move an object up or down by one position.");
        }

        int index = document.Annotations.FindIndex(annotation => annotation.Id == annotationId);
        if (index < 0 || document.Annotations[index].LayerId is not Guid layerId
            || document.Layers.All(layer => layer.Id != layerId))
        {
            return -1;
        }

        for (int neighbor = index + direction; neighbor >= 0 && neighbor < document.Annotations.Count; neighbor += direction)
        {
            if (document.Annotations[neighbor].LayerId == layerId && document.Annotations[neighbor].GroupId == document.Annotations[index].GroupId)
            {
                return neighbor;
            }
        }

        return -1;
    }

    public static IEnumerable<AnnotationObject> InPaintOrder(CaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Layers.Count == 0)
        {
            return document.Annotations;
        }

        return LayerStructure.PaintLayers(document).SelectMany(layer => LayerStructure.PaintObjects(document, layer.Id));
    }

    public static bool IsEffectivelyVisible(CaptureDocument document, AnnotationObject annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        return annotation.IsVisible
            && annotation.LayerId is Guid layerId
            && document.Layers.Any(layer => layer.Id == layerId && !layer.IsFolder && LayerStructure.IsVisible(document, layer))
            && (annotation.GroupId is null || document.ObjectGroups.Any(group => group.Id == annotation.GroupId && group.IsVisible));
    }
}
