using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CaptureDocumentWorkspaceEditorTests
{
    [TestMethod]
    public void SetTitle_TrimsAndStoresTitle()
    {
        var document = new CaptureDocument
        {
            Metadata = new DocumentMetadata(default, default, [])
        };

        CaptureDocumentWorkspaceEditor.SetTitle(document, "  Renamed  ");

        Assert.AreEqual("Renamed", document.Metadata.Properties["title"]);
    }

    [TestMethod]
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
                    IsVisible = false,
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
        var layer = new AnnotationLayer { Name = "Notes", IsVisible = false };
        source.Layers.Add(layer);
        source.Annotations[0].LayerId = layer.Id;

        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(
            source,
            "Copy");

        Assert.AreNotEqual(source.Id, duplicate.Id);
        Assert.AreEqual(source.SourceImage, duplicate.SourceImage);
        Assert.AreEqual("Copy", duplicate.Metadata.Properties["title"]);
        Assert.AreEqual(source.Id.ToString(), duplicate.Metadata.Properties["duplicatedFromDocumentId"]);
        Assert.ContainsSingle(duplicate.Annotations);
        Assert.IsFalse(duplicate.Annotations[0].IsVisible);
        Assert.AreEqual(layer.Id, duplicate.Annotations[0].LayerId);
        Assert.AreEqual(layer.Id, Assert.ContainsSingle(duplicate.Layers).Id);
        Assert.IsFalse(duplicate.Layers[0].IsVisible);
        Assert.ContainsSingle(duplicate.DestructiveOperations);
        Assert.AreEqual(default, duplicate.Metadata.CreatedAtUtc);

        duplicate.DestructiveOperations[0].Parameters["width"] = "20";
        Assert.AreEqual("50", source.DestructiveOperations[0].Parameters["width"]);
        duplicate.Layers[0].Name = "Changed";
        Assert.AreEqual("Notes", source.Layers[0].Name);
    }

    [TestMethod]
    public void SetTitle_UsesFallbackForBlankTitle()
    {
        var document = new CaptureDocument
        {
            Metadata = new DocumentMetadata(default, default, [])
        };

        CaptureDocumentWorkspaceEditor.SetTitle(document, "   ");

        Assert.AreEqual("Untitled Capture", document.Metadata.Properties["title"]);
    }
}
