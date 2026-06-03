namespace SnapStudio.Core.Documents;

public sealed class EditCommandStack : IEditCommandStack
{
    private readonly Stack<IEditCommand> _redoStack = new();
    private readonly Stack<IEditCommand> _undoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public string? UndoDisplayName => CanUndo ? _undoStack.Peek().DisplayName : null;

    public string? RedoDisplayName => CanRedo ? _redoStack.Peek().DisplayName : null;

    public async ValueTask ExecuteAsync(
        CaptureDocument document,
        IEditCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(command);

        await command.ApplyAsync(document, cancellationToken).ConfigureAwait(false);

        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public async ValueTask<bool> UndoAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!CanUndo)
        {
            return false;
        }

        IEditCommand command = _undoStack.Pop();
        try
        {
            await command.RevertAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _undoStack.Push(command);
            throw;
        }

        _redoStack.Push(command);
        return true;
    }

    public async ValueTask<bool> RedoAsync(CaptureDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!CanRedo)
        {
            return false;
        }

        IEditCommand command = _redoStack.Pop();
        try
        {
            await command.ApplyAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _redoStack.Push(command);
            throw;
        }

        _undoStack.Push(command);
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
