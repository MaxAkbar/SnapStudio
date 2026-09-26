using System.Data.Common;
using System.Text.Json;
using CSharpDB.Data;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CSharpDbDocumentStorageTests
{
    [TestMethod]
    public async Task GetAsync_MigratesLegacyDocumentAndKeepsLayerIdAfterSave()
    {
        using var workspace = TemporaryWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var document = new CaptureDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        document.SchemaVersion = 1;
        document.Layers = [];
        document.Annotations.Add(new AnnotationObject { Kind = AnnotationKind.Rectangle });
        await ReplaceDocumentJsonAsync(store, document);

        CaptureDocument? migrated = await repository.GetAsync(document.Id, CancellationToken.None);

        Assert.IsNotNull(migrated);
        Assert.AreEqual(CaptureDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        Guid layerId = Assert.ContainsSingle(migrated.Layers).Id;
        Assert.AreEqual(layerId, Assert.ContainsSingle(migrated.Annotations).LayerId);

        await repository.SaveAsync(migrated, CancellationToken.None);
        CaptureDocument? reloaded = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(layerId, Assert.ContainsSingle(reloaded.Layers).Id);
        Assert.AreEqual(layerId, Assert.ContainsSingle(reloaded.Annotations).LayerId);

        reloaded.Annotations[0].LayerId = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => repository.SaveAsync(reloaded, CancellationToken.None));
        Assert.AreEqual(layerId, Assert.ContainsSingle((await repository.GetAsync(document.Id, CancellationToken.None))!.Annotations).LayerId);
    }

    [TestMethod]
    public async Task GetAndSaveAsync_RejectFutureVersionWithoutChangingStoredDocument()
    {
        using var workspace = TemporaryWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var document = new CaptureDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        document.SchemaVersion = CaptureDocument.CurrentSchemaVersion + 1;
        await ReplaceDocumentJsonAsync(store, document);

        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => repository.GetAsync(document.Id, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => repository.SaveAsync(document, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => repository.SaveAsync(new CaptureDocument { Id = document.Id }, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => repository.GetAsync(document.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateSaveGetAsync_RoundTripsEditableDocument()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        var capture = new CaptureResult(
            CaptureId.New(),
            clock.UtcNow,
            new ImageAsset("source.png", 800, 600, ImagePixelFormat.Rgba32),
            new Dictionary<string, string> { ["source"] = "test" });

        CaptureDocument document = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
        var layer = new AnnotationLayer { Name = "Callouts" };
        document.Layers = [layer];
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Rectangle,
            LayerId = layer.Id,
            IsVisible = false,
            Bounds = new RectD(10, 20, 300, 200),
            Style = AnnotationStyle.Default
        });
        document.Annotations.Add(new AnnotationObject
        {
            Kind = AnnotationKind.Text,
            LayerId = layer.Id,
            Text = "Check this"
        });

        await repository.SaveAsync(document, CancellationToken.None);

        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(document.Id, loaded.Id);
        Assert.AreEqual("source.png", loaded.SourceImage.Path);
        Assert.HasCount(2, loaded.Annotations);
        Assert.AreEqual(AnnotationKind.Rectangle, loaded.Annotations[0].Kind);
        Assert.IsFalse(loaded.Annotations[0].IsVisible);
        Assert.AreEqual(layer.Id, Assert.ContainsSingle(loaded.Layers).Id);
        Assert.AreEqual("Callouts", loaded.Layers[0].Name);
        Assert.IsTrue(loaded.Annotations.All(annotation => annotation.LayerId == layer.Id));
        Assert.AreEqual("test", loaded.Metadata.Properties["source"]);
        Assert.IsTrue(File.Exists(store.DatabasePath));
    }

    [TestMethod]
    public async Task GetRecentAsync_ReturnsDocumentsOrderedByModifiedDate()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        var catalog = new CSharpDbDocumentCatalog(store);

        CaptureDocument older = await CreateDocumentAsync(repository, "Older", clock.UtcNow);
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        CaptureDocument newer = await CreateDocumentAsync(repository, "Newer", clock.UtcNow);

        IReadOnlyList<DocumentSummary> summaries = await catalog.GetRecentAsync(10, CancellationToken.None);

        Assert.AreSequenceEqual([newer.Id, older.Id], summaries.Select(summary => summary.Id));
        Assert.AreEqual("Newer", summaries[0].Title);
        Assert.AreEqual("Older", summaries[1].Title);

        await repository.DeleteAsync(newer.Id, CancellationToken.None);

        summaries = await catalog.GetRecentAsync(10, CancellationToken.None);

        DocumentSummary remaining = Assert.ContainsSingle(summaries);
        Assert.AreEqual(older.Id, remaining.Id);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesDocumentRowAndKeepsSourceImage()
    {
        using var workspace = TemporaryWorkspace.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        await File.WriteAllTextAsync(sourcePath, "image");
        var capture = new CaptureResult(
            CaptureId.New(),
            clock.UtcNow,
            new ImageAsset(sourcePath, 800, 600, ImagePixelFormat.Rgba32),
            new Dictionary<string, string>());

        CaptureDocument document = await repository.CreateFromCaptureAsync(capture, CancellationToken.None);

        bool deleted = await repository.DeleteAsync(document.Id, CancellationToken.None);
        CaptureDocument? loaded = await repository.GetAsync(document.Id, CancellationToken.None);
        bool deletedAgain = await repository.DeleteAsync(document.Id, CancellationToken.None);

        Assert.IsTrue(deleted);
        Assert.IsNull(loaded);
        Assert.IsFalse(deletedAgain);
        Assert.IsTrue(File.Exists(sourcePath));
    }

    private static async Task<CaptureDocument> CreateDocumentAsync(
        IDocumentRepository repository,
        string title,
        DateTimeOffset capturedAtUtc)
    {
        var capture = new CaptureResult(
            CaptureId.New(),
            capturedAtUtc,
            new ImageAsset($"{title}.png", 800, 600, ImagePixelFormat.Rgba32),
            new Dictionary<string, string>
            {
                ["title"] = title,
                ["source"] = "capture"
            });

        return await repository.CreateFromCaptureAsync(capture, CancellationToken.None);
    }

    private static async Task ReplaceDocumentJsonAsync(
        CSharpDbDocumentStore store,
        CaptureDocument document)
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Data Source"] = store.DatabasePath
        };
        await using var connection = new CSharpDbConnection(builder.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET document_json = @json WHERE id = @id";
        DbParameter jsonParameter = command.CreateParameter();
        jsonParameter.ParameterName = "@json";
        jsonParameter.Value = JsonSerializer.Serialize(
            document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        command.Parameters.Add(jsonParameter);
        DbParameter idParameter = command.CreateParameter();
        idParameter.ParameterName = "@id";
        idParameter.Value = document.Id.ToString();
        command.Parameters.Add(idParameter);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
