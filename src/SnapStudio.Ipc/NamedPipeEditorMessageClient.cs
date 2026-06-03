using System.IO.Pipes;
using System.Text;
using SnapStudio.Core.Messaging;

namespace SnapStudio.Ipc;

public sealed class NamedPipeEditorMessageClient : IEditorMessageClient
{
    private readonly TimeSpan _connectionTimeout;
    private readonly string _pipeName;
    private readonly EditorMessageSerializer _serializer;

    public NamedPipeEditorMessageClient(
        string pipeName,
        EditorMessageSerializer? serializer = null,
        TimeSpan? connectionTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        _pipeName = pipeName;
        _serializer = serializer ?? new EditorMessageSerializer();
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(2);
    }

    public async Task<EditorMessageSendResult> SendAsync(
        EditorMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectionTimeout);

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);

            await using var writer = new StreamWriter(
                client,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: false)
            {
                AutoFlush = true
            };

            string json = _serializer.Serialize(message);
            await writer.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false);
            await writer.FlushAsync(timeout.Token).ConfigureAwait(false);

            return EditorMessageSendResult.Success();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EditorMessageSendResult.Failed(
                $"Timed out connecting to editor IPC pipe '{_pipeName}'.");
        }
        catch (IOException exception)
        {
            return EditorMessageSendResult.Failed(exception.Message);
        }
    }
}
