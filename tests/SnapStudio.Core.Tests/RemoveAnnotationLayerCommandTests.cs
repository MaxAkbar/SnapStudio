using SnapStudio.Core.Documents;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class RemoveAnnotationLayerCommandTests
{
    [TestMethod]
    public async Task DeleteLayer_RemovesItsObjectsAndRestoresTheirOrderOnUndo()
    {
        var document = new CaptureDocument();
        AnnotationLayer bottom = document.Layers[0];
        var middle = new AnnotationLayer { Name = "Middle" };
        var top = new AnnotationLayer { Name = "Top" };
        document.Layers.AddRange([middle, top]);

        var bottomFirst = new AnnotationObject { LayerId = bottom.Id };
        var middleFirst = new AnnotationObject { LayerId = middle.Id };
        var topFirst = new AnnotationObject { LayerId = top.Id };
        var middleSecond = new AnnotationObject { LayerId = middle.Id };
        var bottomSecond = new AnnotationObject { LayerId = bottom.Id };
        document.Annotations.AddRange([bottomFirst, middleFirst, topFirst, middleSecond, bottomSecond]);

        var stack = new EditCommandStack();
        await stack.ExecuteAsync(
            document,
            new RemoveAnnotationLayerCommand(middle.Id),
            CancellationToken.None);

        Assert.AreSequenceEqual(
            new[] { bottom.Id, top.Id },
            document.Layers.Select(layer => layer.Id).ToArray());
        Assert.AreSequenceEqual(
            new[] { bottomFirst.Id, topFirst.Id, bottomSecond.Id },
            document.Annotations.Select(annotation => annotation.Id).ToArray());
        DocumentLayers.ValidateForSave(document);

        Assert.IsTrue(await stack.UndoAsync(document, CancellationToken.None));
        Assert.AreSequenceEqual(
            new[] { bottom.Id, middle.Id, top.Id },
            document.Layers.Select(layer => layer.Id).ToArray());
        Assert.AreSame(middle, document.Layers[1]);
        Assert.AreSequenceEqual(
            new[] { bottomFirst.Id, middleFirst.Id, topFirst.Id, middleSecond.Id, bottomSecond.Id },
            document.Annotations.Select(annotation => annotation.Id).ToArray());
        DocumentLayers.ValidateForSave(document);

        Assert.IsTrue(await stack.RedoAsync(document, CancellationToken.None));
        Assert.AreEqual(2, document.Layers.Count);
        Assert.AreEqual(3, document.Annotations.Count);
        DocumentLayers.ValidateForSave(document);
    }

    [TestMethod]
    public async Task DeleteLayer_RefusesToRemoveTheLastLayer()
    {
        var document = new CaptureDocument();
        document.Annotations.Add(new AnnotationObject { LayerId = document.Layers[0].Id });
        var stack = new EditCommandStack();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            stack.ExecuteAsync(
                document,
                new RemoveAnnotationLayerCommand(document.Layers[0].Id),
                CancellationToken.None).AsTask());

        Assert.ContainsSingle(document.Layers);
        Assert.ContainsSingle(document.Annotations);
        Assert.IsFalse(stack.CanUndo);
        DocumentLayers.ValidateForSave(document);
    }
}
