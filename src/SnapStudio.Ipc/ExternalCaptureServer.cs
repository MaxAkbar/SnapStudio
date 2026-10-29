using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SnapStudio.Core.Capture;

namespace SnapStudio.Ipc;

/// <summary>Separate, bounded request/reply channel; older editor notifications are unaffected.</summary>
public sealed class ExternalCaptureServer(string pipeName)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string DefaultPipeName => Environment.GetEnvironmentVariable("SNAPSTUDIO_INTEGRATION_PIPE")
        ?? $"SnapStudio.ExternalCapture.v1.Session{(OperatingSystem.IsWindows() ? Process.GetCurrentProcess().SessionId : 0)}";

    public async Task RunAsync(Func<ExternalCaptureRequest, CancellationToken, Task<ExternalCaptureResponse>> capture, CancellationToken lifetime)
    {
        while (!lifetime.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(lifetime).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(50));
            var id = Guid.Empty;
            try
            {
                var request = JsonSerializer.Deserialize<ExternalCaptureRequest>(await ReadLineAsync(pipe, deadline.Token), JsonOptions)
                    ?? throw new InvalidDataException("Missing capture request.");
                id = request.RequestId; request.Validate();
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                Task disconnected = WatchDisconnectAsync(pipe, operation);
                ExternalCaptureResponse response;
                try { response = await capture(request, operation.Token).ConfigureAwait(false); }
                finally { operation.Cancel(); await disconnected.ConfigureAwait(false); }
                await WriteLineAsync(pipe, response, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or OperationCanceledException or InvalidOperationException
                or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException or NotSupportedException)
            {
                if (lifetime.IsCancellationRequested) break;
                try
                {
                    await WriteLineAsync(pipe, new ExternalCaptureResponse(1, id, false,
                    error is OperationCanceledException ? "Capture timed out or was cancelled." : error.Message), deadline.Token);
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

    public static async Task<string> ReadLineAsync(Stream pipe, CancellationToken token)
    {
        using var text = new MemoryStream(); var next = new byte[1];
        while (text.Length < 8192)
        {
            if (await pipe.ReadAsync(next, token).ConfigureAwait(false) == 0) throw new EndOfStreamException("The capture connection closed.");
            if (next[0] == (byte)'\n') return Encoding.UTF8.GetString(text.ToArray()).TrimEnd('\r');
            text.WriteByte(next[0]);
        }
        throw new InvalidDataException("The capture message is too large.");
    }
    public static Task WriteLineAsync<T>(Stream pipe, T message, CancellationToken token) =>
        pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions) + "\n"), token).AsTask();
}
