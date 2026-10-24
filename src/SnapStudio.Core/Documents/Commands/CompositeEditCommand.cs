namespace SnapStudio.Core.Documents;

/// <summary>A selection edit is one undo step, including rollback if any child fails.</summary>
public sealed class CompositeEditCommand(string displayName, IReadOnlyList<IEditCommand> commands) : IEditCommand
{
    private readonly IEditCommand[] _commands = commands.ToArray();
    public string DisplayName => displayName;

    public async ValueTask ApplyAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        int applied = 0;
        try
        {
            for (; applied < _commands.Length; applied++)
                await _commands[applied].ApplyAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            while (applied > 0) await _commands[--applied].RevertAsync(document, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask RevertAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        int next = _commands.Length - 1;
        try
        {
            for (; next >= 0; next--) await _commands[next].RevertAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            for (int index = next + 1; index < _commands.Length; index++)
                await _commands[index].ApplyAsync(document, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
