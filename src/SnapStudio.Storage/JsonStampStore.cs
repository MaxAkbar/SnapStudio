using System.Text;
using System.Text.Json;
using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

public sealed class JsonStampStore(string path) : IStampStore
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<StampDefinition>> LoadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("The stamp library exceeds 8 MB.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        StampPack? pack = await JsonSerializer.DeserializeAsync<StampPack>(stream, Options, token).ConfigureAwait(false);
        if (pack?.SchemaVersion != StampPack.CurrentSchemaVersion || pack.Stamps is null)
            throw new InvalidDataException("This is not a supported SnapStudio stamp library.");
        return StampCatalog.ValidateLibrary(pack.Stamps);
    }

    public async Task SaveAsync(IReadOnlyList<StampDefinition> stamps, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<StampDefinition> validated = StampCatalog.ValidateLibrary(stamps);
            string json = JsonSerializer.Serialize(new StampPack(StampPack.CurrentSchemaVersion, validated), Options);
            if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The stamp library exceeds 8 MB.");
            string absolute = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            string temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, json, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, absolute, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }
}
