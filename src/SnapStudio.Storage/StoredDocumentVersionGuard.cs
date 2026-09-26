using System.Text.Json;
using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

internal static class StoredDocumentVersionGuard
{
    public static void EnsureWritable(string json)
    {
        using JsonDocument stored = JsonDocument.Parse(json);
        JsonElement root = stored.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The stored document is invalid.");
        }

        int? schemaVersion = null;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (schemaVersion is not null
                || property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out int version))
            {
                throw new InvalidDataException("The stored document schema version is ambiguous or invalid.");
            }

            schemaVersion = version;
        }

        if (schemaVersion is null)
        {
            return;
        }

        if (schemaVersion > CaptureDocument.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Document schema version {schemaVersion} is newer than this app supports ({CaptureDocument.CurrentSchemaVersion}).");
        }

        if (schemaVersion < 1)
        {
            throw new InvalidDataException($"Document schema version {schemaVersion} is invalid.");
        }
    }
}
