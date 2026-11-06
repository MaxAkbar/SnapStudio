using System.IO.Pipes;
using System.Text.Json;
using SnapStudio.Core.Integration;

namespace SnapStudio.Ipc;

public sealed class WorkspaceCommandClient(string pipeName)
{
    public async Task<WorkspaceCommandResponse> SendAsync(WorkspaceCommandRequest request, TimeSpan connectionTimeout, CancellationToken cancellationToken)
    {
        request.Validate();
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(connectionTimeout);
            try { await pipe.ConnectAsync(connect.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("SnapStudio is not ready. Open SnapStudio, finish setup or any active capture, and try again.");
            }
        }
        // Never retry after sending: a timed-out request may already have created a document.
        await ExternalCaptureServer.WriteLineAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<WorkspaceCommandResponse>(
            await ExternalCaptureServer.ReadLineAsync(pipe, cancellationToken).ConfigureAwait(false), ExternalCaptureServer.JsonOptions)
            ?? throw new InvalidDataException("SnapStudio returned an empty response.");
        if (response.ProtocolVersion != 1 || response.RequestId != request.RequestId)
            throw new InvalidDataException("SnapStudio returned an incompatible response. Update both apps and try again.");
        return response;
    }
}
