using SnapStudio.Core.Capture;
using SnapStudio.Core.Messaging;
using SnapStudio.Core.Primitives;
using SnapStudio.Ipc;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class EditorMessageSerializerTests
{
    [TestMethod]
    public void Deserialize_RoundTripsCaptureCompletedMessage()
    {
        var serializer = new EditorMessageSerializer();
        var message = new CaptureCompletedEditorMessage(
            CaptureId.New(),
            DocumentId.New(),
            @"C:\captures\source.png");

        string json = serializer.Serialize(message);
        EditorMessage deserialized = serializer.Deserialize(json);

        var completed = Assert.IsExactInstanceOfType<CaptureCompletedEditorMessage>(deserialized);
        Assert.AreEqual(message.CaptureId, completed.CaptureId);
        Assert.AreEqual(message.DocumentId, completed.DocumentId);
        Assert.AreEqual(message.SourceImagePath, completed.SourceImagePath);
        Assert.AreEqual(CaptureCompletedEditorMessage.TypeName, completed.MessageType);
    }

    [TestMethod]
    public void Deserialize_RoundTripsCaptureFailedMessage()
    {
        var serializer = new EditorMessageSerializer();
        var message = new CaptureFailedEditorMessage(
            CaptureFailureReason.NotImplemented,
            "Still capture is not implemented.");

        string json = serializer.Serialize(message);
        EditorMessage deserialized = serializer.Deserialize(json);

        var failed = Assert.IsExactInstanceOfType<CaptureFailedEditorMessage>(deserialized);
        Assert.AreEqual(CaptureFailureReason.NotImplemented, failed.Reason);
        Assert.AreEqual("Still capture is not implemented.", failed.Message);
        Assert.AreEqual(CaptureFailedEditorMessage.TypeName, failed.MessageType);
    }
}
