namespace SnapStudio.Core.Messaging;

public sealed class NoOpEditorMessageClient : IEditorMessageClient
{
    public Task<EditorMessageSendResult> SendAsync(
        EditorMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EditorMessageSendResult.Success());
    }
}
