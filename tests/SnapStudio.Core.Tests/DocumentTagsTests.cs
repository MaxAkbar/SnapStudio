using System.Data.Common;
using System.Text.Json;
using CSharpDB.Data;
using SnapStudio.Core.Documents;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentTagsTests
{
    [TestMethod]
    public void Parse_NormalizesSeparatorsWhitespaceAndCaseDuplicates()
    {
        Assert.AreSequenceEqual(new[] { "Design", "To review", "日本語", "C#" },
            DocumentTags.Parse("  Design, design ; To   review\n日本語\r\nDESIGN, C#"));
    }

    [TestMethod]
    public void Set_RejectsInvalidTagsWithoutChangingMetadata()
    {
        var document = new CaptureDocument();
        DocumentTags.Set(document, ["Existing"]);
        DocumentMetadata before = document.Metadata;
        foreach (string[] tags in new[]
        {
            new[] { new string('a', DocumentTags.MaximumLength + 1) }, new[] { "a;b" }, new[] { "a,b" }
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() => DocumentTags.Set(document, tags));
            Assert.AreSame(before, document.Metadata);
        }
        DocumentTags.Set(document, [new string('a', DocumentTags.MaximumLength)]);
        Assert.AreEqual(DocumentTags.MaximumLength, DocumentTags.Read(document).Single().Length);
    }

    [TestMethod]
    public void Tags_HaveNoCountLimitAndStillDeduplicate()
    {
        string[] tags = Enumerable.Range(0, 1000).Select(index => $"Tag {index}").ToArray();
        var document = new CaptureDocument();
        DocumentTags.Set(document, tags.Concat(tags.Select(tag => tag.ToUpperInvariant())));
        Assert.AreSequenceEqual(tags, DocumentTags.Read(document));
    }

    [TestMethod]
    public void Tags_AreOptionalAndMalformedStoredValuesDoNotBreakReading()
    {
        var document = new CaptureDocument();
        Assert.IsEmpty(DocumentTags.Read(document));
        foreach (string json in new[] { "broken", "{}", "null", "[123]" })
        {
            document.Metadata.Properties["tags"] = json;
            Assert.IsEmpty(DocumentTags.Read(document));
        }
    }

    [TestMethod]
    public void Duplicate_InheritsIndependentTagsAndKeepsOtherMetadata()
    {
        var document = new CaptureDocument();
        document.Metadata.Properties["title"] = "Original";
        DocumentTags.Set(document, ["Design", "To review"]);
        CaptureDocument duplicate = CaptureDocumentWorkspaceEditor.CreateDuplicate(document, "Copy");
        Assert.AreSequenceEqual(DocumentTags.Read(document), DocumentTags.Read(duplicate));
        DocumentTags.Set(duplicate, ["Approved"]);
        Assert.AreSequenceEqual(new[] { "Design", "To review" }, DocumentTags.Read(document));
        DocumentTags.Set(duplicate, []);
        Assert.IsFalse(duplicate.Metadata.Properties.ContainsKey("tags"));
        Assert.AreEqual("Copy", duplicate.Metadata.Properties["title"]);
        Assert.AreEqual(document.Id.ToString(), duplicate.Metadata.Properties["duplicatedFromDocumentId"]);
    }

    [TestMethod]
    public async Task FileStorage_RoundTripsTagsInDocumentAndCatalog()
    {
        using var workspace = TemporaryWorkspace.Create();
        await CheckRoundTripAsync(new FileSystemDocumentRepository(workspace.Path), new FileSystemDocumentCatalog(workspace.Path));
    }

    [TestMethod]
    public async Task Database_RoundTripsTagsAndPreservesThemWhenSmallLibraryIsRebuilt()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        await CheckRoundTripAsync(new CSharpDbDocumentRepository(store), new CSharpDbDocumentCatalog(store));
        var document = new CaptureDocument();
        DocumentTags.Set(document, ["Survivor"]);
        await store.SaveAsync(document, CancellationToken.None);
        var removed = new CaptureDocument();
        await store.SaveAsync(removed, CancellationToken.None);
        await store.DeleteAsync(removed.Id, CancellationToken.None);
        DocumentSummary summary = (await new CSharpDbDocumentStore(workspace.Path).GetRecentAsync(10, CancellationToken.None)).Single();
        Assert.AreSequenceEqual(new[] { "Survivor" }, summary.Tags);
    }

    [TestMethod]
    public async Task Database_UpgradesExistingTableAndRetainsDocuments()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var old = new CaptureDocument();
        await using (var connection = new CSharpDbConnection(new CSharpDbConnectionStringBuilder { DataSource = store.DatabasePath, Pooling = true }.ConnectionString))
        {
            await connection.OpenAsync();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE documents (id TEXT PRIMARY KEY, created_at_utc TEXT NOT NULL, modified_at_utc TEXT NOT NULL, title TEXT NOT NULL, source_image_path TEXT NOT NULL, annotation_count INTEGER NOT NULL, source_kind TEXT NOT NULL, document_json TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "INSERT INTO documents VALUES (@id, @created, @modified, 'Old capture', 'old.png', 0, 'capture', @json)";
            foreach ((string name, string value) in new[] { ("@id", old.Id.ToString()), ("@created", DateTimeOffset.UtcNow.ToString("O")), ("@modified", DateTimeOffset.UtcNow.ToString("O")), ("@json", JsonSerializer.Serialize(old, new JsonSerializerOptions(JsonSerializerDefaults.Web))) })
            {
                DbParameter parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync();
        }
        DocumentSummary summary = (await store.GetRecentAsync(10, CancellationToken.None)).Single();
        Assert.AreEqual(old.Id, summary.Id);
        Assert.IsEmpty(summary.Tags);
        CaptureDocument loaded = (await store.GetAsync(old.Id, CancellationToken.None))!;
        DocumentTags.Set(loaded, ["Migrated"]);
        await store.SaveAsync(loaded, CancellationToken.None);
        Assert.AreSequenceEqual(new[] { "Migrated" }, (await new CSharpDbDocumentStore(workspace.Path).GetRecentAsync(10, CancellationToken.None)).Single().Tags);
    }

    private static async Task CheckRoundTripAsync(IDocumentRepository repository, IDocumentCatalog catalog)
    {
        var document = new CaptureDocument();
        DocumentTags.Set(document, new[] { "Design", "To review", "日本語" }.Concat(
            Enumerable.Range(0, 100).Select(index => $"Tag {index}")));
        await repository.SaveAsync(document, CancellationToken.None);
        Assert.AreSequenceEqual(DocumentTags.Read(document), DocumentTags.Read((await repository.GetAsync(document.Id, CancellationToken.None))!));
        Assert.AreSequenceEqual(DocumentTags.Read(document), (await catalog.GetRecentAsync(10, CancellationToken.None)).Single().Tags);
        DocumentTags.Set(document, []);
        await repository.SaveAsync(document, CancellationToken.None);
        Assert.IsEmpty((await catalog.GetRecentAsync(10, CancellationToken.None)).Single().Tags);
        await repository.DeleteAsync(document.Id, CancellationToken.None);
        Assert.IsEmpty(await catalog.GetRecentAsync(10, CancellationToken.None));
    }
}
