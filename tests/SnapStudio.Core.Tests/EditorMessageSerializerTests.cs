using SnapStudio.Core.Capture;
using SnapStudio.Core.Messaging;
using SnapStudio.Core.Primitives;
using SnapStudio.Ipc;

namespace SnapStudio.Core.Tests;

public sealed class EditorMessageSerializerTests
{
    [Fact]
    public void Deserialize_RoundTripsCaptureCompletedMessage()
    {
        var serializer = new EditorMessageSerializer();
        var message = new CaptureCompletedEditorMessage(
            CaptureId.New(),
            DocumentId.New(),
            @"C:\captures\source.png");

        string json = serializer.Serialize(message);
        EditorMessage deserialized = serializer.Deserialize(json);

        var completed = Assert.IsType<CaptureCompletedEditorMessage>(deserialized);
        Assert.Equal(message.CaptureId, completed.CaptureId);
        Assert.Equal(message.DocumentId, completed.DocumentId);
        Assert.Equal(message.SourceImagePath, completed.SourceImagePath);
        Assert.Equal(CaptureCompletedEditorMessage.TypeName, completed.MessageType);
    }

    [Fact]
    public void Deserialize_RoundTripsCaptureFailedMessage()
    {
        var serializer = new EditorMessageSerializer();
        var message = new CaptureFailedEditorMessage(
            CaptureFailureReason.NotImplemented,
            "Still capture is not implemented.");

        string json = serializer.Serialize(message);
        EditorMessage deserialized = serializer.Deserialize(json);

        var failed = Assert.IsType<CaptureFailedEditorMessage>(deserialized);
        Assert.Equal(CaptureFailureReason.NotImplemented, failed.Reason);
        Assert.Equal("Still capture is not implemented.", failed.Message);
        Assert.Equal(CaptureFailedEditorMessage.TypeName, failed.MessageType);
    }
}
