using System.Data.Common;
using System.Text.Json;
using CSharpDB.Data;
using SnapStudio.Core.Documents;

namespace SnapStudio.Storage;

public sealed partial class CSharpDbDocumentStore : IHistoryFolderStore
{
    private readonly SemaphoreSlim _historyFolderGate = new(1, 1);

    public async Task<HistoryFolderState> LoadFoldersAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using CSharpDbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT state_json FROM history_folders WHERE id = 1";
        string? json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null ? new() : JsonSerializer.Deserialize<HistoryFolderState>(json, JsonOptions)
            ?? throw new InvalidDataException("The capture folders could not be read.");
    }

    public async Task SaveFoldersAsync(HistoryFolderState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        string json = JsonSerializer.Serialize(state, JsonOptions);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await _historyFolderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using CSharpDbConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM history_folders WHERE id = 1";
            bool exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
            // Retain the row when updating so an empty folder list never deletes the table's last leaf.
            await ExecuteNonQueryAsync(connection, transaction, exists
                ? "UPDATE history_folders SET state_json = @json WHERE id = 1"
                : "INSERT INTO history_folders (id, state_json) VALUES (1, @json)",
                [new("@json", json)], cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _historyFolderGate.Release(); }
    }
}
