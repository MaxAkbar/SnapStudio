using System.Text.Json;
using SnapStudio.Core.Messaging;

namespace SnapStudio.Ipc;

public sealed class EditorMessageSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize(EditorMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var envelope = new EditorMessageEnvelope(
            message.ProtocolVersion,
            message.MessageId,
            message.CreatedAtUtc,
            message.MessageType,
            JsonSerializer.SerializeToElement(message, message.GetType(), JsonOptions));

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public EditorMessage Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        EditorMessageEnvelope? envelope = JsonSerializer.Deserialize<EditorMessageEnvelope>(json, JsonOptions);
        if (envelope is null)
        {
            throw new InvalidOperationException("Editor message envelope could not be deserialized.");
        }

        if (envelope.ProtocolVersion != EditorMessage.CurrentProtocolVersion)
        {
            throw new NotSupportedException(
                $"Editor message protocol version {envelope.ProtocolVersion} is not supported.");
        }

        return envelope.MessageType switch
        {
            PingEditorMessage.TypeName => DeserializePayload<PingEditorMessage>(envelope),
            CaptureCompletedEditorMessage.TypeName => DeserializePayload<CaptureCompletedEditorMessage>(envelope),
            CaptureFailedEditorMessage.TypeName => DeserializePayload<CaptureFailedEditorMessage>(envelope),
            _ => throw new NotSupportedException($"Editor message type '{envelope.MessageType}' is not supported.")
        };
    }

    private static TMessage DeserializePayload<TMessage>(EditorMessageEnvelope envelope)
        where TMessage : EditorMessage
    {
        TMessage? message = envelope.Payload.Deserialize<TMessage>(JsonOptions);
        return message ?? throw new InvalidOperationException(
            $"Editor message payload '{envelope.MessageType}' could not be deserialized.");
    }

    private sealed record EditorMessageEnvelope(
        int ProtocolVersion,
        Guid MessageId,
        DateTimeOffset CreatedAtUtc,
        string MessageType,
        JsonElement Payload);
}
