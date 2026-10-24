using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentFolderImporterTests
{
    [TestMethod]
    public void DiscoveryIncludesSupportedImagesAndOnlyRequestedSubfolders()
    {
        using var workspace = TemporaryWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "nested"));
        foreach (string name in new[] { "A.PNG", "b.jpg", "c.jpeg", "d.bmp", "notes.txt", "nested/e.png" })
            File.WriteAllText(Path.Combine(workspace.Path, name), "fixture");
        Assert.HasCount(4, DocumentFolderImporter.FindImages(workspace.Path, false));
        Assert.HasCount(5, DocumentFolderImporter.FindImages(workspace.Path, true));
    }

    [TestMethod]
    public async Task ImportGroupsEverySuccessfulImageAndReportsBrokenFiles()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var state = new HistoryFolderState();
        HistoryFolder folder = state.AddFolder("Imported");
        await store.SaveFoldersAsync(state, CancellationToken.None);
        var importer = new DocumentFolderImporter(new FixtureImages(), repository, store);
        FolderImportResult result = await importer.ImportAsync(["one.png", "bad.png", "two.jpg"], state, folder.Id, null, CancellationToken.None);

        Assert.HasCount(2, result.ImportedDocuments);
        Assert.AreEqual("bad.png", Assert.ContainsSingle(result.Failures).FileName);
        Assert.IsNull(result.ErrorMessage);
        HistoryFolderState persisted = await store.LoadFoldersAsync(CancellationToken.None);
        foreach (DocumentId id in result.ImportedDocuments)
        {
            Assert.AreEqual(folder.Id, persisted.GetFolderId(id));
            Assert.IsNotNull(await repository.GetAsync(id, CancellationToken.None));
        }
        Assert.HasCount(2, await new CSharpDbDocumentCatalog(store).GetRecentAsync(20, CancellationToken.None));
    }

    [TestMethod]
    public async Task RefreshAddsOnlyNewPathsAndPreservesExistingEditsAndManualMoves()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var state = new HistoryFolderState();
        HistoryFolder mapped = state.AddFolder("Mapped", Path.Combine(workspace.Path, "External"));
        HistoryFolder group = state.AddFolder("My edits");
        var importer = new DocumentFolderImporter(new FixtureImages(), repository, store);
        string one = Path.Combine(mapped.SourcePath!, "one.png");
        string two = Path.Combine(mapped.SourcePath!, "two.png");
        FolderImportResult initial = await importer.ImportAsync([one, two, one.ToUpperInvariant()], state, mapped.Id, null, CancellationToken.None);
        Assert.HasCount(2, initial.ImportedDocuments);
        CaptureDocument edited = (await repository.GetAsync(initial.ImportedDocuments[0], CancellationToken.None))!;
        edited.Metadata.Properties["title"] = "My edited image";
        edited.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle, LayerId = edited.Layers[0].Id });
        await repository.SaveAsync(edited, CancellationToken.None);
        state.MoveDocument(edited.Id, group.Id);
        await store.SaveFoldersAsync(state, CancellationToken.None);

        HistoryFolderState reopened = await store.LoadFoldersAsync(CancellationToken.None);
        FolderImportResult refreshed = await importer.ImportAsync([one, two, Path.Combine(mapped.SourcePath!, "new.png")], reopened, mapped.Id, null, CancellationToken.None);
        Assert.HasCount(1, refreshed.ImportedDocuments);
        Assert.AreEqual(group.Id, reopened.GetFolderId(edited.Id));
        CaptureDocument retained = (await repository.GetAsync(edited.Id, CancellationToken.None))!;
        Assert.AreEqual("My edited image", retained.Metadata.Properties["title"]);
        Assert.HasCount(1, retained.Annotations);
        Assert.HasCount(3, await new CSharpDbDocumentCatalog(store).GetRecentAsync(20, CancellationToken.None));
        Assert.HasCount(3, (await store.LoadFoldersAsync(CancellationToken.None)).LinkedImages);
    }

    [TestMethod]
    public async Task MappedImportResumesAfterCancellationWithoutDuplicatingCompletedFiles()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var state = new HistoryFolderState();
        HistoryFolder mapped = state.AddFolder("Mapped", Path.Combine(workspace.Path, "External"));
        string[] paths = [Path.Combine(mapped.SourcePath!, "one.png"), Path.Combine(mapped.SourcePath!, "two.png")];
        var first = new DocumentFolderImporter(new FixtureImages(), repository, new CancelAfterSave(store, cancellation));
        FolderImportResult interrupted = await first.ImportAsync(paths, state, mapped.Id, null, cancellation.Token);
        Assert.IsTrue(interrupted.WasCancelled);
        DocumentId original = Assert.ContainsSingle(interrupted.ImportedDocuments);
        HistoryFolderState reopened = await store.LoadFoldersAsync(CancellationToken.None);
        var retry = new DocumentFolderImporter(new FixtureImages(), repository, store);
        FolderImportResult resumed = await retry.ImportAsync(paths, reopened, mapped.Id, null, CancellationToken.None);
        Assert.HasCount(1, resumed.ImportedDocuments);
        Assert.AreNotEqual(original, resumed.ImportedDocuments[0]);
        Assert.HasCount(2, await new CSharpDbDocumentCatalog(store).GetRecentAsync(20, CancellationToken.None));
        FolderImportResult unchanged = await retry.ImportAsync(paths, await store.LoadFoldersAsync(CancellationToken.None), mapped.Id, null, CancellationToken.None);
        Assert.IsEmpty(unchanged.ImportedDocuments);
    }

    [TestMethod]
    public async Task CancellingKeepsAlreadyImportedImagesAndTheirFolderMembership()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var state = new HistoryFolderState();
        HistoryFolder folder = state.AddFolder("Partial import");
        await store.SaveFoldersAsync(state, CancellationToken.None);
        var importer = new DocumentFolderImporter(new FixtureImages(), new CSharpDbDocumentRepository(store), new CancelAfterSave(store, cancellation));
        FolderImportResult result = await importer.ImportAsync(["one.png", "two.png", "three.png"], state, folder.Id, null, cancellation.Token);

        Assert.IsTrue(result.WasCancelled);
        DocumentId imported = Assert.ContainsSingle(result.ImportedDocuments);
        Assert.AreEqual(folder.Id, (await store.LoadFoldersAsync(CancellationToken.None)).GetFolderId(imported));
        Assert.AreEqual(imported, Assert.ContainsSingle(await new CSharpDbDocumentCatalog(store).GetRecentAsync(20, CancellationToken.None)).Id);
    }

    private sealed class FixtureImages : IImageImportService
    {
        public Task<CaptureOutcome> ImportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CaptureOutcome> ImportFileAsync(string path, CancellationToken cancellationToken) => Task.FromResult(path == "bad.png"
            ? CaptureOutcome.Failed(new CaptureFailure(CaptureFailureReason.Unknown, "Broken image"))
            : CaptureOutcome.Success(new CaptureResult(CaptureId.New(), DateTimeOffset.UtcNow,
                new ImageAsset(path, 20, 10, ImagePixelFormat.Bgra32), new Dictionary<string, string> { ["title"] = path, ["source"] = "import" })));
    }

    private sealed class CancelAfterSave(IHistoryFolderStore store, CancellationTokenSource cancellation) : IHistoryFolderStore
    {
        public Task<HistoryFolderState> LoadFoldersAsync(CancellationToken token) => store.LoadFoldersAsync(token);
        public async Task SaveFoldersAsync(HistoryFolderState state, CancellationToken token)
        {
            await store.SaveFoldersAsync(state, token);
            cancellation.Cancel();
        }
    }
}
