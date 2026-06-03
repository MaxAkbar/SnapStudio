using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class CaptureDocumentWorkspaceEditorTests
{
    [Fact]
    public void SetTitle_TrimsAndStoresTitle()
    {
        var document = new CaptureDocument
        {
            Metadata = new DocumentMetadata(default, default, [])
        };

        CaptureDocumentWorkspaceEditor.SetTitle(document, "  Renamed  ");

        Assert.Equal("Renamed", document.Metadata.Properties["title"]);
    }

    [Fact]
    public void CreateDuplicate_CopiesEditableStateWithNewIdentity()
    {
        var source = new CaptureDocument
        {
            Id = DocumentId.New(),
            SourceImage = new ImageAsset("source.png", 100, 80, ImagePixelFormat.Bgra32),
            Metadata = new DocumentMetadata(
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-02T00:00:00Z"),
                new Dictionary<string, string> { ["title"] = "Original" }),
            Annotations =
            [
                new AnnotationObject
                {
                    Id = Guid.NewGuid(),
                    Kind = AnnotationKind.Rectangle,
                    Bounds = new RectD(1, 2, 3, 4)
                }
            ],
            DestructiveOperations =
            [
                new DestructiveEditOperation(
                    Guid.NewGuid(),
                    DestructiveOperationKind.Resize,
                    null,
                    new Dictionary<string, string> { ["width"] = "50" })
            ]
        };

        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(
            source,
            "Copy");

        Assert.NotEqual(source.Id, duplicate.Id);
        Assert.Equal(source.SourceImage, duplicate.SourceImage);
        Assert.Equal("Copy", duplicate.Metadata.Properties["title"]);
        Assert.Equal(source.Id.ToString(), duplicate.Metadata.Properties["duplicatedFromDocumentId"]);
        Assert.Single(duplicate.Annotations);
        Assert.Single(duplicate.DestructiveOperations);
        Assert.Equal(default, duplicate.Metadata.CreatedAtUtc);

        duplicate.DestructiveOperations[0].Parameters["width"] = "20";
        Assert.Equal("50", source.DestructiveOperations[0].Parameters["width"]);
    }

    [Fact]
    public void SetTitle_UsesFallbackForBlankTitle()
    {
        var document = new CaptureDocument
        {
            Metadata = new DocumentMetadata(default, default, [])
        };

        CaptureDocumentWorkspaceEditor.SetTitle(document, "   ");

        Assert.Equal("Untitled Capture", document.Metadata.Properties["title"]);
    }
}
