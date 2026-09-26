using SnapStudio.Core.Documents;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentLayersTests
{
    [TestMethod]
    public void NewDocument_StartsWithValidDefaultLayer()
    {
        var document = new CaptureDocument();

        Assert.ContainsSingle(document.Layers);
        Assert.AreNotEqual(Guid.Empty, document.Layers[0].Id);
        DocumentLayers.ValidateForSave(document);
    }

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
    public void EnsureInitialized_MigratesEarlierLayerlessVersionTwoDocument()
    {
        var document = new CaptureDocument
        {
            Layers = [],
            Annotations = [new AnnotationObject { Kind = AnnotationKind.Text }]
        };

        DocumentLayers.EnsureInitialized(document);

        Guid layerId = Assert.ContainsSingle(document.Layers).Id;
        Assert.AreEqual(layerId, Assert.ContainsSingle(document.Annotations).LayerId);
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
    public void EnsureInitialized_RejectsFutureVersionWithoutDowngrading()
    {
        var document = new CaptureDocument
        {
            SchemaVersion = CaptureDocument.CurrentSchemaVersion + 1
        };
        Guid originalLayerId = document.Layers[0].Id;

        Assert.ThrowsExactly<NotSupportedException>(() => DocumentLayers.EnsureInitialized(document));

        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion + 1, document.SchemaVersion);
        Assert.AreEqual(originalLayerId, Assert.ContainsSingle(document.Layers).Id);
    }

    [TestMethod]
    public void EnsureInitialized_DoesNotRepairCurrentVersionOrphan()
    {
        var orphan = new AnnotationObject { LayerId = Guid.NewGuid() };
        var document = new CaptureDocument { Annotations = [orphan] };

        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.EnsureInitialized(document));

        Assert.AreEqual(orphan.LayerId, document.Annotations[0].LayerId);
        Assert.IsFalse(DocumentLayers.IsEffectivelyVisible(document, orphan));
    }

    [TestMethod]
    public void ValidateForSave_RejectsDuplicateLayerAndAnnotationIds()
    {
        var document = new CaptureDocument();
        Guid layerId = document.Layers[0].Id;
        document.Layers.Add(new AnnotationLayer { Id = layerId });

        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));

        document.Layers.RemoveAt(1);
        Guid annotationId = Guid.NewGuid();
        document.Annotations.Add(new AnnotationObject { Id = annotationId, LayerId = layerId });
        document.Annotations.Add(new AnnotationObject { Id = annotationId, LayerId = layerId });

        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));
    }

    [TestMethod]
    public void ValidateForSave_RejectsUnassignedAnnotationWithoutChangingIt()
    {
        var annotation = new AnnotationObject();
        var document = new CaptureDocument { Annotations = [annotation] };

        Assert.ThrowsExactly<InvalidDataException>(() => DocumentLayers.ValidateForSave(document));

        Assert.IsNull(annotation.LayerId);
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
