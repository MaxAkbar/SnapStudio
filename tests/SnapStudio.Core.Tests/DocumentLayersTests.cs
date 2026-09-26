using SnapStudio.Core.Documents;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentLayersTests
{
    [TestMethod]
    public void EnsureInitialized_MigratesLegacyAnnotationsIntoOneLayer()
    {
        var first = new AnnotationObject { Kind = AnnotationKind.Rectangle };
        var second = new AnnotationObject { Kind = AnnotationKind.Text };
        var document = new CaptureDocument
        {
            SchemaVersion = 1,
            Annotations = [first, second]
        };

        DocumentLayers.EnsureInitialized(document);
        DocumentLayers.EnsureInitialized(document);

        AnnotationLayer layer = Assert.ContainsSingle(document.Layers);
        Assert.AreEqual(layer.Id, first.LayerId);
        Assert.AreEqual(layer.Id, second.LayerId);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, document.SchemaVersion);
    }

    [TestMethod]
    public void InPaintOrder_GroupsObjectsByLayerAndRespectsVisibility()
    {
        var bottom = new AnnotationLayer { Name = "Bottom" };
        var top = new AnnotationLayer { Name = "Top", IsVisible = false };
        var topObject = new AnnotationObject { LayerId = top.Id };
        var bottomFirst = new AnnotationObject { LayerId = bottom.Id };
        var bottomSecond = new AnnotationObject { LayerId = bottom.Id };
        var document = new CaptureDocument
        {
            Layers = [bottom, top],
            Annotations = [topObject, bottomFirst, bottomSecond]
        };

        Assert.AreSequenceEqual(
            [bottomFirst.Id, bottomSecond.Id, topObject.Id],
            DocumentLayers.InPaintOrder(document).Select(annotation => annotation.Id));
        Assert.IsFalse(DocumentLayers.IsEffectivelyVisible(document, topObject));
        Assert.IsTrue(DocumentLayers.IsEffectivelyVisible(document, bottomFirst));
    }

    [TestMethod]
    public async Task Commands_AddMoveAndHideLayer_SupportUndoRedo()
    {
        var bottom = new AnnotationLayer { Name = "Bottom" };
        var top = new AnnotationLayer { Name = "Top" };
        var annotation = new AnnotationObject { LayerId = bottom.Id };
        var existingTopObject = new AnnotationObject { LayerId = top.Id };
        var document = new CaptureDocument
        {
            Layers = [bottom],
            Annotations = [annotation, existingTopObject]
        };
        var stack = new EditCommandStack();

        await stack.ExecuteAsync(document, new AddAnnotationLayerCommand(top), CancellationToken.None);
        await stack.ExecuteAsync(document,
            new UpdateAnnotationLayerCommand(annotation.Id, bottom.Id, top.Id),
            CancellationToken.None);
        await stack.ExecuteAsync(document,
            new UpdateLayerVisibilityCommand(top.Id, true, false),
            CancellationToken.None);

        Assert.AreEqual(top.Id, annotation.LayerId);
        Assert.AreSame(annotation, document.Annotations[^1]);
        Assert.IsFalse(DocumentLayers.IsEffectivelyVisible(document, annotation));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.IsTrue(DocumentLayers.IsEffectivelyVisible(document, annotation));
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreEqual(bottom.Id, annotation.LayerId);
        Assert.AreSame(annotation, document.Annotations[0]);
        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.ContainsSingle(document.Layers);
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual(top.Id, annotation.LayerId);
        Assert.IsFalse(DocumentLayers.IsEffectivelyVisible(document, annotation));
    }
}
