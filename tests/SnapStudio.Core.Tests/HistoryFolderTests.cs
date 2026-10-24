using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class HistoryFolderTests
{
    [TestMethod]
    public void FolderNamesAreTrimmedUniqueAndValidatedBeforeChangingState()
    {
        var state = new HistoryFolderState();
        HistoryFolder folder = state.AddFolder("  Project A  ");
        Assert.AreEqual("Project A", folder.Name);
        foreach (string invalid in new[] { "", " ", "project a", "All captures", "UNFILED", new string('x', 81) })
            Assert.ThrowsExactly<ArgumentException>(() => state.AddFolder(invalid));
        state.RenameFolder(folder.Id, " project A ");
        Assert.AreEqual(folder.Id, Assert.ContainsSingle(state.Folders).Id);
    }

    [TestMethod]
    public void MovingRenamingAndRemovingFoldersPreservesDocumentIdentity()
    {
        var state = new HistoryFolderState();
        HistoryFolder first = state.AddFolder("First");
        HistoryFolder second = state.AddFolder("Second");
        DocumentId one = DocumentId.New(), two = DocumentId.New();
        state.MoveDocument(one, first.Id);
        state.MoveDocument(two, first.Id);
        HistoryFolderState original = state.Copy();
        state.MoveDocument(one, second.Id);
        state.RenameFolder(second.Id, "Renamed");
        Assert.AreEqual(second.Id, state.GetFolderId(one));
        Assert.AreEqual(first.Id, original.GetFolderId(one));
        state.RemoveFolder(first.Id);
        Assert.IsNull(state.GetFolderId(two));
        Assert.AreEqual(second.Id, state.GetFolderId(one));
        state.MoveDocument(one, null);
        Assert.IsNull(state.GetFolderId(one));
        Assert.IsEmpty(state.Assignments);
        Assert.ThrowsExactly<ArgumentException>(() => state.MoveDocument(one, Guid.NewGuid()));
    }

    [TestMethod]
    public async Task DatabaseFoldersRoundTripWithoutModifyingDocumentsAndSurviveDocumentRemoval()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var document = new CaptureDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        DateTimeOffset modified = document.Metadata.ModifiedAtUtc;
        Assert.IsEmpty((await store.LoadFoldersAsync(CancellationToken.None)).Folders);
        var state = new HistoryFolderState();
        HistoryFolder folder = state.AddFolder("Work");
        state.MoveDocument(document.Id, folder.Id);
        await store.SaveFoldersAsync(state, CancellationToken.None);

        var reopened = new CSharpDbDocumentStore(workspace.Path);
        HistoryFolderState loaded = await reopened.LoadFoldersAsync(CancellationToken.None);
        Assert.AreEqual("Work", Assert.ContainsSingle(loaded.Folders).Name);
        Assert.AreEqual(folder.Id, loaded.GetFolderId(document.Id));
        Assert.AreEqual(modified, (await repository.GetAsync(document.Id, CancellationToken.None))!.Metadata.ModifiedAtUtc);

        await repository.DeleteAsync(document.Id, CancellationToken.None);
        Assert.AreEqual(folder.Id, Assert.ContainsSingle((await store.LoadFoldersAsync(CancellationToken.None)).Folders).Id);
        Assert.IsEmpty(await new CSharpDbDocumentCatalog(store).GetRecentAsync(10, CancellationToken.None));
    }

    [TestMethod]
    public async Task MappedFoldersKeepIndependentLocationsAndRememberFilesAfterReopening()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var state = new HistoryFolderState();
        string firstPath = Path.Combine(workspace.Path, "First");
        string secondPath = Path.Combine(workspace.Path, "Second");
        HistoryFolder first = state.AddFolder("First map", firstPath, includeSubfolders: true);
        HistoryFolder second = state.AddFolder("Second map", secondPath);
        Assert.ThrowsExactly<ArgumentException>(() => state.AddFolder("Same location", firstPath.ToUpperInvariant() + Path.DirectorySeparatorChar));
        DocumentId document = DocumentId.New();
        string filePath = Path.Combine(firstPath, "nested", "photo.png");
        state.MoveDocument(document, first.Id);
        state.RememberLinkedImage(first.Id, filePath, document);
        await store.SaveFoldersAsync(state, CancellationToken.None);

        HistoryFolderState reopened = await new CSharpDbDocumentStore(workspace.Path).LoadFoldersAsync(CancellationToken.None);
        Assert.AreEqual(firstPath, reopened.Folders.Single(folder => folder.Id == first.Id).SourcePath);
        Assert.IsTrue(reopened.Folders.Single(folder => folder.Id == first.Id).IncludeSubfolders);
        Assert.AreEqual(secondPath, reopened.Folders.Single(folder => folder.Id == second.Id).SourcePath);
        Assert.IsTrue(reopened.IsLinkedImageKnown(first.Id, filePath.ToUpperInvariant()));
        Assert.IsFalse(reopened.IsLinkedImageKnown(second.Id, filePath));
        HistoryFolderState copy = reopened.Copy();
        copy.RemoveFolder(first.Id);
        Assert.IsEmpty(copy.LinkedImages);
        Assert.IsNull(copy.GetFolderId(document));
        Assert.HasCount(1, reopened.LinkedImages);
    }

    [TestMethod]
    public async Task DatabaseCanGrowRenameShrinkAndReuseEmptyFolderState()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var state = new HistoryFolderState();
        HistoryFolder folder = state.AddFolder("Large group");
        for (int index = 0; index < 150; index++) state.MoveDocument(DocumentId.New(), folder.Id);
        await store.SaveFoldersAsync(state, CancellationToken.None);
        state.RenameFolder(folder.Id, "Renamed large group");
        await store.SaveFoldersAsync(state, CancellationToken.None);
        Assert.HasCount(150, (await store.LoadFoldersAsync(CancellationToken.None)).Assignments);
        state.RemoveFolder(folder.Id);
        await store.SaveFoldersAsync(state, CancellationToken.None);
        Assert.IsEmpty((await store.LoadFoldersAsync(CancellationToken.None)).Folders);
        HistoryFolder replacement = state.AddFolder("Replacement");
        await store.SaveFoldersAsync(state, CancellationToken.None);
        Assert.AreEqual(replacement, Assert.ContainsSingle((await store.LoadFoldersAsync(CancellationToken.None)).Folders));
    }
}
