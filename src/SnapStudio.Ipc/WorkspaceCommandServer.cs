using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SnapStudio.Core.Integration;

namespace SnapStudio.Ipc;

/// <summary>Optional launcher requests never require PowerToys assemblies in the editor.</summary>
public sealed class WorkspaceCommandServer(string pipeName)
{
    public static string DefaultPipeName => Environment.GetEnvironmentVariable("SNAPSTUDIO_COMMAND_PIPE")
        ?? $"SnapStudio.WorkspaceCommands.v1.Session{(OperatingSystem.IsWindows() ? Process.GetCurrentProcess().SessionId : 0)}";

    public async Task RunAsync(Func<WorkspaceCommandRequest, CancellationToken, Task<WorkspaceCommandResponse>> execute, CancellationToken lifetime)
    {
        while (!lifetime.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(lifetime).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            Guid id = Guid.Empty;
            try
            {
                var request = JsonSerializer.Deserialize<WorkspaceCommandRequest>(
                    await ExternalCaptureServer.ReadLineAsync(pipe, deadline.Token).ConfigureAwait(false), ExternalCaptureServer.JsonOptions)
                    ?? throw new InvalidDataException("Missing workspace command.");
                id = request.RequestId;
                request.Validate();
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                Task disconnected = WatchDisconnectAsync(pipe, operation);
                WorkspaceCommandResponse response;
                try { response = await execute(request, operation.Token).ConfigureAwait(false); }
                finally { operation.Cancel(); await disconnected.ConfigureAwait(false); }
                await ExternalCaptureServer.WriteLineAsync(pipe, response, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException
                or OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.ExternalException
                or UnauthorizedAccessException or NotSupportedException)
            {
                if (lifetime.IsCancellationRequested) break;
                try
                {
                    await ExternalCaptureServer.WriteLineAsync(pipe, new WorkspaceCommandResponse(1, id, false,
                        error is OperationCanceledException ? "The command timed out or was cancelled." : error.Message), deadline.Token).ConfigureAwait(false);
                }
                catch (Exception sendError) when (sendError is IOException or OperationCanceledException or InvalidOperationException) { }
            }
        }
    }

    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource operation)
    {
        try { await pipe.ReadAsync(new byte[1], operation.Token).ConfigureAwait(false); operation.Cancel(); }
        catch (Exception error) when (error is IOException or OperationCanceledException) { operation.Cancel(); }
    }
}
