using System.Text.Json;
using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

public sealed class FileSystemDocumentCatalog : IDocumentCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _rootPath;

    public FileSystemDocumentCatalog(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<IReadOnlyList<DocumentSummary>> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0 || !Directory.Exists(_rootPath))
        {
            return [];
        }

        var summaries = new List<DocumentSummary>();

        foreach (string documentDirectory in Directory.EnumerateDirectories(_rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string documentPath = Path.Combine(
                documentDirectory,
                FileSystemDocumentNames.DocumentFileName);

            if (!File.Exists(documentPath))
            {
                continue;
            }

            CaptureDocument? document = await LoadDocumentAsync(documentPath, cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                continue;
            }

            summaries.Add(DocumentSummaryFactory.Create(document, documentDirectory));
        }

        return summaries
            .OrderByDescending(summary => summary.ModifiedAtUtc)
            .ThenByDescending(summary => summary.CreatedAtUtc)
            .Take(maximumCount)
            .ToArray();
    }

    private static async Task<CaptureDocument?> LoadDocumentAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(documentPath);
            return await JsonSerializer
                .DeserializeAsync<CaptureDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
