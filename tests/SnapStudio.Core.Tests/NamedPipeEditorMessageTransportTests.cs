using SnapStudio.Core.Messaging;
using SnapStudio.Ipc;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class NamedPipeEditorMessageTransportTests
{
    [TestMethod]
    public async Task SendAsync_ReceiveOneAsync_RoundTripsMessage()
    {
        string pipeName = $"SnapStudio.Tests.{Guid.NewGuid():N}";
        var server = new NamedPipeEditorMessageServer(pipeName);
        var client = new NamedPipeEditorMessageClient(pipeName, connectionTimeout: TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task<EditorMessage> receiveTask = server.ReceiveOneAsync(timeout.Token);

        EditorMessageSendResult sendResult = await client.SendAsync(
            new PingEditorMessage("transport-test"),
            timeout.Token);

        EditorMessage received = await receiveTask.WaitAsync(timeout.Token);

        Assert.IsTrue(sendResult.Succeeded, sendResult.ErrorMessage);
        var ping = Assert.IsExactInstanceOfType<PingEditorMessage>(received);
        Assert.AreEqual("transport-test", ping.Sender);
    }
}
