using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Ipc;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ExternalCaptureTests
{
    [TestMethod]
    public void PhysicalPixelRequestsAllowNegativeMonitorOriginsAndRejectInvalidGeometry()
    {
        var request = new ExternalCaptureRequest(1, Guid.NewGuid(), -1920, -200, 1280, 720, "Portrait guide");
        request.Validate(); Assert.AreEqual(new RectD(-1920, -200, 1280, 720), request.Bounds);
        foreach (var invalid in new[] { request with { ProtocolVersion = 2 }, request with { RequestId = Guid.Empty }, request with { Width = 0 },
            request with { Height = -10 }, request with { Width = 20001 }, request with { Width = 10000, Height = 10000 }, request with { X = int.MaxValue },
            request with { Name = "Bad\nname" }, request with { Name = new string('a', 121) } })
            Assert.ThrowsExactly<InvalidDataException>(invalid.Validate);
    }
    [TestMethod]
    public void RegionMustFitOneRealDisplayWithoutClampingOrDpiScaling()
    {
        RectD[] monitors = [new(-1920, 0, 1920, 1080), new(0, 0, 3840, 2160)];
        Assert.IsTrue(WindowsFixedRegionCaptureTargetSelector.FitsDisplay(new(-1800, 20, 1280, 720), monitors));
        Assert.IsTrue(WindowsFixedRegionCaptureTargetSelector.FitsDisplay(new(0, 0, 3840, 2160), monitors));
        Assert.IsFalse(WindowsFixedRegionCaptureTargetSelector.FitsDisplay(new(-100, 0, 400, 400), monitors));
        Assert.IsFalse(WindowsFixedRegionCaptureTargetSelector.FitsDisplay(new(3800, 0, 400, 400), monitors));
        Assert.IsFalse(WindowsFixedRegionCaptureTargetSelector.FitsDisplay(new(0, 0, 0, 0), monitors));
    }
    [TestMethod]
    public async Task ServerRepliesWithCorrelationAndContinuesAfterMalformedAndFailedRequests()
    {
        string pipeName = "SnapStudio.test." + Guid.NewGuid().ToString("N"); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = new List<ExternalCaptureRequest>();
        Task server = new ExternalCaptureServer(pipeName).RunAsync((request, token) =>
        {
            received.Add(request);
            if (request.Name == "fail") throw new InvalidOperationException("Capture unavailable");
            return Task.FromResult(new ExternalCaptureResponse(1, request.RequestId, true, null, Guid.NewGuid()));
        }, lifetime.Token);
        try
        {
            Assert.IsFalse((await Send("{not valid json")).Succeeded);
            var failed = new ExternalCaptureRequest(1, Guid.NewGuid(), -1000, 0, 800, 600, "fail");
            Assert.IsFalse((await Send(JsonSerializer.Serialize(failed with { Width = 0 }, ExternalCaptureServer.JsonOptions))).Succeeded);
            Assert.AreEqual("Capture unavailable", (await Send(JsonSerializer.Serialize(failed, ExternalCaptureServer.JsonOptions))).Error);
            var valid = failed with { RequestId = Guid.NewGuid(), Name = "ExactFrame" };
            var reply = await Send(JsonSerializer.Serialize(valid, ExternalCaptureServer.JsonOptions));
            Assert.IsTrue(reply.Succeeded); Assert.AreEqual(valid.RequestId, reply.RequestId); Assert.IsNotNull(reply.DocumentId);
            Assert.HasCount(2, received); Assert.AreEqual(valid.Bounds, received[1].Bounds);
        }
        finally { lifetime.Cancel(); try { await server; } catch (OperationCanceledException) { } }

        async Task<ExternalCaptureResponse> Send(string json)
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(lifetime.Token); await client.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), lifetime.Token);
            return JsonSerializer.Deserialize<ExternalCaptureResponse>(await ExternalCaptureServer.ReadLineAsync(client, lifetime.Token), ExternalCaptureServer.JsonOptions)!;
        }
    }
    [TestMethod]
    public async Task DisconnectCancelsInFlightCaptureAndListenerAcceptsTheNextClient()
    {
        string pipeName = "SnapStudio.cancel." + Guid.NewGuid().ToString("N"); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        Task server = new ExternalCaptureServer(pipeName).RunAsync(async (request, token) =>
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
            var request = new ExternalCaptureRequest(1, Guid.NewGuid(), 0, 0, 800, 600, "Frame");
            await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                await client.ConnectAsync(lifetime.Token); await ExternalCaptureServer.WriteLineAsync(client, request, lifetime.Token);
                await entered.Task.WaitAsync(lifetime.Token);
            }
            await cancelled.Task.WaitAsync(lifetime.Token);
            await using var second = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await second.ConnectAsync(lifetime.Token); await ExternalCaptureServer.WriteLineAsync(second, request, lifetime.Token);
            Assert.IsTrue(JsonSerializer.Deserialize<ExternalCaptureResponse>(await ExternalCaptureServer.ReadLineAsync(second, lifetime.Token), ExternalCaptureServer.JsonOptions)!.Succeeded);
        }
        finally { lifetime.Cancel(); try { await server; } catch (OperationCanceledException) { } }
    }
    [TestMethod]
    public async Task TransportRejectsOversizedInputAndHonorsCancellation()
    {
        using var oversized = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 8193)));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ExternalCaptureServer.ReadLineAsync(oversized, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var input = new MemoryStream([10]);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => ExternalCaptureServer.ReadLineAsync(input, cancelled.Token));
    }
}
