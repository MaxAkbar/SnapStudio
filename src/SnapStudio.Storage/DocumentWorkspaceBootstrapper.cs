using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.System;

namespace SnapStudio.Storage;

public sealed class DocumentWorkspaceBootstrapper : IDocumentWorkspaceBootstrapper
{
    private readonly IDocumentCatalog _catalog;
    private readonly IClock _clock;
    private readonly IDocumentRepository _repository;
    private readonly string _rootPath;

    public DocumentWorkspaceBootstrapper(
        string rootPath,
        IDocumentRepository repository,
        IDocumentCatalog catalog,
        IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(catalog);

        _rootPath = Path.GetFullPath(rootPath);
        _repository = repository;
        _catalog = catalog;
        _clock = clock ?? new SystemClock();
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootPath);

        IReadOnlyList<DocumentSummary> documents = await _catalog
            .GetRecentAsync(1, cancellationToken)
            .ConfigureAwait(false);

        if (documents.Count > 0)
        {
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        var document = new CaptureDocument
        {
            SourceImage = new ImageAsset("seed://welcome", 1280, 720, ImagePixelFormat.Unknown),
            Metadata = new DocumentMetadata(
                now,
                now,
                new Dictionary<string, string>
                {
                    ["title"] = "Welcome Capture",
                    ["source"] = "seed",
                    ["description"] = "Seed document used to validate workspace persistence."
                })
        };

        await _repository.SaveAsync(document, cancellationToken).ConfigureAwait(false);
    }
}
