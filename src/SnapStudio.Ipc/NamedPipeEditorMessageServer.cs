using System.IO.Pipes;
using System.Text;
using SnapStudio.Core.Messaging;

namespace SnapStudio.Ipc;

public sealed class NamedPipeEditorMessageServer
{
    private readonly string _pipeName;
    private readonly EditorMessageSerializer _serializer;

    public NamedPipeEditorMessageServer(
        string pipeName,
        EditorMessageSerializer? serializer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        _pipeName = pipeName;
        _serializer = serializer ?? new EditorMessageSerializer();
    }

    public async Task<EditorMessage> ReceiveOneAsync(CancellationToken cancellationToken)
    {
        await using var server = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(
            server,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        string? json = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new EndOfStreamException("Editor IPC pipe closed before a message was received.");
        }

        return _serializer.Deserialize(json);
    }

    public async Task RunAsync(
        IEditorMessageHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        while (!cancellationToken.IsCancellationRequested)
        {
            EditorMessage message = await ReceiveOneAsync(cancellationToken).ConfigureAwait(false);
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
