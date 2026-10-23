using System.Drawing;
using System.Drawing.Imaging;
using CSharpDB.Data;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using SnapStudio.Rendering;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CSharpDbConnectionConcurrencyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SaveAsync_WhenReadOrThumbnailOverlaps_ClosesConnectionsWithoutWalConflict(bool generateThumbnail)
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        using var clock = new BlockingClock();
        var store = new CSharpDbDocumentStore(workspace.Path, clock);
        var repository = new CSharpDbDocumentRepository(store);
        string sourcePath = Path.Combine(workspace.Path, "source.png");
        using (var bitmap = new Bitmap(16, 12, PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(8, 6, Color.Red);
            bitmap.Save(sourcePath, ImageFormat.Png);
        }

        CaptureDocument document = await repository.CreateFromCaptureAsync(new CaptureResult(
            CaptureId.New(), clock.UtcNow,
            new ImageAsset(sourcePath, 16, 12, ImagePixelFormat.Bgra32),
            new Dictionary<string, string> { ["title"] = "Before" }), CancellationToken.None);
        document.Metadata.Properties["title"] = "After";

        // Save reads the clock after opening its connection and before beginning the write transaction.
        // Hold that connection open while another operation reads and closes its own connection.
        clock.BlockNextRead();
        Task save = Task.Run(() => repository.SaveAsync(document, CancellationToken.None));
        try
        {
            Assert.IsTrue(clock.WaitUntilBlocked(), "Save did not reach the open-connection boundary.");
            if (generateThumbnail)
            {
                var cache = new FileSystemDocumentThumbnailCache(
                    workspace.Path, repository, new SystemDrawingDocumentRenderer(repository));
                DocumentThumbnailResult result = await cache.EnsureThumbnailAsync(
                    new DocumentThumbnailRequest(document.Id, document.Metadata.ModifiedAtUtc, 64),
                    CancellationToken.None);
                Assert.IsTrue(result.Succeeded, result.ErrorMessage);
                Assert.IsTrue(File.Exists(result.ThumbnailPath));
            }
            else
            {
                CaptureDocument? read = await repository.GetAsync(document.Id, CancellationToken.None);
                Assert.IsNotNull(read);
                Assert.AreEqual("Before", read.Metadata.Properties["title"]);
            }
        }
        catch
        {
            // Failed provider disposal can leave a file handle open; retain evidence without hiding the failure.
            workspace.PreserveEvidence();
            throw;
        }
        finally
        {
            clock.Release();
            await save;
        }

        CaptureDocument? saved = await repository.GetAsync(document.Id, CancellationToken.None);
        Assert.IsNotNull(saved);
        Assert.AreEqual("After", saved.Metadata.Properties["title"]);
    }

    [TestMethod]
    public async Task SaveAsync_AfterPoolRetirement_DocumentCanBeReopened()
    {
        using var workspace = CSharpDbTestWorkspace.Create();
        var store = new CSharpDbDocumentStore(workspace.Path);
        var repository = new CSharpDbDocumentRepository(store);
        var document = new CaptureDocument
        {
            Metadata = new DocumentMetadata(default, default, new Dictionary<string, string> { ["title"] = "Persisted" })
        };
        await repository.SaveAsync(document, CancellationToken.None);

        var builder = new CSharpDbConnectionStringBuilder { DataSource = store.DatabasePath, Pooling = true };
        await CSharpDbConnection.ClearPoolAsync(builder.ConnectionString);
        var reopened = new CSharpDbDocumentRepository(new CSharpDbDocumentStore(workspace.Path));
        CaptureDocument? loaded = await reopened.GetAsync(document.Id, CancellationToken.None);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Persisted", loaded.Metadata.Properties["title"]);
    }

    private sealed class BlockingClock : IClock, IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private int _blockNextRead;

        public DateTimeOffset UtcNow
        {
            get
            {
                if (Interlocked.Exchange(ref _blockNextRead, 0) == 1)
                {
                    _entered.Set();
                    if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The overlapping database operation did not complete.");
                    }
                }

                return DateTimeOffset.UtcNow;
            }
        }

        public void BlockNextRead() => Interlocked.Exchange(ref _blockNextRead, 1);

        public bool WaitUntilBlocked() => _entered.Wait(TimeSpan.FromSeconds(5));

        public void Release() => _release.Set();

        public void Dispose()
        {
            _entered.Dispose();
            _release.Dispose();
        }
    }
}
