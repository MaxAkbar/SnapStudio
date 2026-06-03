namespace SnapStudio.Core.Messaging;

public interface IEditorMessageClient
{
    Task<EditorMessageSendResult> SendAsync(
        EditorMessage message,
        CancellationToken cancellationToken);
}

public interface IEditorMessageHandler
{
    Task HandleAsync(EditorMessage message, CancellationToken cancellationToken);
}
