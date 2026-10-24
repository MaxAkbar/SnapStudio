using System.Text.Json;
using System.Text.Json.Serialization;
using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

public sealed class JsonToolDesignStore(string path) : IToolDesignStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<ToolDesign>> LoadAsync(CancellationToken cancellationToken) =>
        File.Exists(path) ? await ReadPackAsync(path, cancellationToken).ConfigureAwait(false) : [];

    public async Task SaveAsync(IReadOnlyList<ToolDesign> designs, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WritePackAsync(path, designs, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public static async Task<IReadOnlyList<ToolDesign>> ReadPackAsync(string path, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The tool design file is too large (maximum 8 MB).");
        await using var stream = File.OpenRead(path);
        ToolDesignPack? pack = await JsonSerializer.DeserializeAsync<ToolDesignPack>(stream, Options, cancellationToken).ConfigureAwait(false);
        if (pack?.SchemaVersion != ToolDesignPack.CurrentSchemaVersion || pack.Designs is null)
            throw new InvalidDataException("This is not a supported SnapStudio tool design file.");
        return ToolDesigns.ValidateLibrary(pack.Designs);
    }

    public static async Task WritePackAsync(string path, IReadOnlyList<ToolDesign> designs, CancellationToken cancellationToken)
    {
        List<ToolDesign> validated = ToolDesigns.ValidateLibrary(designs);
        string absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        string temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string json = JsonSerializer.Serialize(new ToolDesignPack(ToolDesignPack.CurrentSchemaVersion, validated), Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidDataException("The tool design file exceeds 8 MB.");
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, absolute, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
