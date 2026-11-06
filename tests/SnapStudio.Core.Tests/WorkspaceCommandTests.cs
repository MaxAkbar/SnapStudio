using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SnapStudio.Core.Integration;
using SnapStudio.Core.Primitives;
using SnapStudio.Ipc;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WorkspaceCommandTests
{
    [TestMethod]
    public void OnlyKnownVersionedCommandsAreAccepted()
    {
        foreach (string command in new[] { WorkspaceCommandRequest.CaptureRegion, WorkspaceCommandRequest.EditClipboard, WorkspaceCommandRequest.OpenLatest })
            new WorkspaceCommandRequest(1, Guid.NewGuid(), command).Validate();
        var request = new WorkspaceCommandRequest(1, Guid.NewGuid(), WorkspaceCommandRequest.OpenLatest);
        foreach (var invalid in new[] { request with { ProtocolVersion = 2 }, request with { RequestId = Guid.Empty },
            request with { Command = "run-program" }, request with { Command = "" }, request with { Command = null! } })
            Assert.ThrowsExactly<InvalidDataException>(invalid.Validate);
    }

    [TestMethod]
    public async Task ListenerRecoversAfterMalformedUnknownAndFailedCommands()
    {
        string pipe = NewPipe();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int calls = 0;
        Guid document = Guid.NewGuid();
        Task server = new WorkspaceCommandServer(pipe).RunAsync((request, _) =>
        {
            calls++;
            if (request.Command == WorkspaceCommandRequest.EditClipboard) throw new InvalidOperationException("The clipboard has no image.");
            return Task.FromResult(new WorkspaceCommandResponse(1, request.RequestId, true, null, document));
        }, lifetime.Token);
        try
        {
            Assert.IsFalse((await RawSendAsync(pipe, "{bad json", lifetime.Token)).Succeeded);
            var invalid = new WorkspaceCommandRequest(1, Guid.NewGuid(), "unknown");
            Assert.IsFalse((await RawSendAsync(pipe, JsonSerializer.Serialize(invalid, ExternalCaptureServer.JsonOptions), lifetime.Token)).Succeeded);
            var client = new WorkspaceCommandClient(pipe);
            var request = new WorkspaceCommandRequest(1, Guid.NewGuid(), WorkspaceCommandRequest.EditClipboard);
            Assert.AreEqual("The clipboard has no image.", (await client.SendAsync(request, TimeSpan.FromSeconds(2), lifetime.Token)).Error);
            request = request with { RequestId = Guid.NewGuid(), Command = WorkspaceCommandRequest.OpenLatest };
            var response = await client.SendAsync(request, TimeSpan.FromSeconds(2), lifetime.Token);
            Assert.IsTrue(response.Succeeded);
            Assert.AreEqual(request.RequestId, response.RequestId);
            Assert.AreEqual(document, response.DocumentId);
            Assert.AreEqual(2, calls);
        }
        finally { lifetime.Cancel(); await StopAsync(server); }
    }

    [TestMethod]
    public async Task CancellingTheClientCancelsTheOperationAndAllowsAnotherCommand()
    {
        string pipe = NewPipe();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var commandLifetime = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task server = new WorkspaceCommandServer(pipe).RunAsync(async (request, token) =>
        {
            if (++calls == 1)
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cancelled.SetResult(); }
            }
            return new(1, request.RequestId, true, null);
        }, lifetime.Token);
        try
        {
            var request = new WorkspaceCommandRequest(1, Guid.NewGuid(), WorkspaceCommandRequest.CaptureRegion);
            var client = new WorkspaceCommandClient(pipe);
            Task sending = client.SendAsync(request, TimeSpan.FromSeconds(2), commandLifetime.Token);
            await entered.Task.WaitAsync(lifetime.Token);
            commandLifetime.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await sending);
            await cancelled.Task.WaitAsync(lifetime.Token);
            Assert.IsTrue((await client.SendAsync(request with { RequestId = Guid.NewGuid() }, TimeSpan.FromSeconds(2), lifetime.Token)).Succeeded);
            Assert.AreEqual(2, calls);
        }
        finally { lifetime.Cancel(); await StopAsync(server); }
    }

    [TestMethod]
    public async Task MissingEditorTimesOutWithoutStartingAnything()
    {
        var request = new WorkspaceCommandRequest(1, Guid.NewGuid(), WorkspaceCommandRequest.OpenLatest);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => new WorkspaceCommandClient(NewPipe()).SendAsync(request, TimeSpan.FromMilliseconds(50), default));
    }

    [TestMethod]
    public async Task LauncherUsesTheRunningConnectionWithoutAnExecutablePath()
    {
        string pipe = NewPipe();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int calls = 0;
        Task server = new WorkspaceCommandServer(pipe).RunAsync((request, _) =>
        {
            calls++;
            return Task.FromResult(new WorkspaceCommandResponse(1, request.RequestId, false, "Finish the current dialog."));
        }, lifetime.Token);
        try
        {
            var result = await new WorkspaceCommandLauncher(pipe).ExecuteAsync(WorkspaceCommandRequest.OpenLatest, "invalid executable", lifetime.Token);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("Finish the current dialog.", result.Error);
            Assert.AreEqual(1, calls);
        }
        finally { lifetime.Cancel(); await StopAsync(server); }
    }

    [TestMethod]
    [DataRow(2, false)]
    [DataRow(1, true)]
    public async Task ClientRejectsIncompatibleOrUncorrelatedReplies(int version, bool wrongId)
    {
        string name = NewPipe();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var request = new WorkspaceCommandRequest(1, Guid.NewGuid(), WorkspaceCommandRequest.OpenLatest);
        Task receiving = new WorkspaceCommandClient(name).SendAsync(request, TimeSpan.FromSeconds(2), lifetime.Token);
        await pipe.WaitForConnectionAsync(lifetime.Token);
        await ExternalCaptureServer.ReadLineAsync(pipe, lifetime.Token);
        await ExternalCaptureServer.WriteLineAsync(pipe, new WorkspaceCommandResponse(version, wrongId ? Guid.NewGuid() : request.RequestId, true, null), lifetime.Token);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await receiving);
    }

    [TestMethod]
    [DataRow("#4CC2FF", 76, 194, 255)]
    [DataRow("  aBcDeF\r\n", 171, 205, 239)]
    [DataRow("#aBc", 170, 187, 204)]
    [DataRow("000", 0, 0, 0)]
    public void ClipboardColorsAcceptOpaqueRgbFormats(string text, int r, int g, int b)
    {
        Assert.IsTrue(ClipboardColor.TryParse(text, out var color));
        Assert.AreEqual(new ColorRgba((byte)r, (byte)g, (byte)b, 255), color);
    }

    [TestMethod]
    public void UnrelatedClipboardTextDoesNotChangeAColor()
    {
        foreach (string? text in new[] { null, "", "red", "#FF0000FF", "rgb(255,0,0)", "#12345", "-12345", "hello", "# 12345", new string('a', 5000) })
            Assert.IsFalse(ClipboardColor.TryParse(text, out _), text);
    }

    private static string NewPipe() => "SnapStudio.workspace.test." + Guid.NewGuid().ToString("N");
    private static async Task StopAsync(Task server) { try { await server; } catch (OperationCanceledException) { } }
    private static async Task<WorkspaceCommandResponse> RawSendAsync(string name, string json, CancellationToken token)
    {
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(token);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), token);
        return JsonSerializer.Deserialize<WorkspaceCommandResponse>(await ExternalCaptureServer.ReadLineAsync(pipe, token), ExternalCaptureServer.JsonOptions)!;
    }
}
