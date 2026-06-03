using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

public static class CaptureDocumentWorkspaceEditor
{
    private const string TitlePropertyName = "title";
    private const string DuplicatedFromPropertyName = "duplicatedFromDocumentId";

    public static void SetTitle(CaptureDocument document, string title)
    {
        ArgumentNullException.ThrowIfNull(document);

        string normalizedTitle = NormalizeTitle(title);
        var properties = new Dictionary<string, string>(document.Metadata.Properties)
        {
            [TitlePropertyName] = normalizedTitle
        };

        document.Metadata = document.Metadata with
        {
            Properties = properties
        };
    }

    public static CaptureDocument CreateDuplicate(
        CaptureDocument source,
        string title)
    {
        ArgumentNullException.ThrowIfNull(source);

        var properties = new Dictionary<string, string>(source.Metadata.Properties)
        {
            [TitlePropertyName] = NormalizeTitle(title),
            [DuplicatedFromPropertyName] = source.Id.ToString()
        };

        return new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = source.SourceImage,
            Metadata = new DocumentMetadata(default, default, properties),
            Annotations = CloneAnnotations(source.Annotations),
            DestructiveOperations = CloneDestructiveOperations(source.DestructiveOperations),
            ExportSettings = source.ExportSettings
        };
    }

    private static string NormalizeTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title)
            ? "Untitled Capture"
            : title.Trim();
    }

    private static List<AnnotationObject> CloneAnnotations(
        IReadOnlyCollection<AnnotationObject> annotations)
    {
        return annotations
            .Select(annotation => new AnnotationObject
            {
                Id = annotation.Id,
                Kind = annotation.Kind,
                Bounds = annotation.Bounds,
                Text = annotation.Text,
                Style = annotation.Style
            })
            .ToList();
    }

    private static List<DestructiveEditOperation> CloneDestructiveOperations(
        IReadOnlyCollection<DestructiveEditOperation> operations)
    {
        return operations
            .Select(operation => operation with
            {
                Parameters = new Dictionary<string, string>(operation.Parameters)
            })
            .ToList();
    }
}
