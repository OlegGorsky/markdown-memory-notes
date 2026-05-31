using System.Text.Json;

namespace Notes.Core.Sync;

public static class SyncAckMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(string messageId)
    {
        if (!SyncMessageId.IsValid(messageId))
        {
            throw new ArgumentException("Sync message id is not valid.", nameof(messageId));
        }

        return JsonSerializer.Serialize(new AckMessage("ack", messageId), JsonOptions);
    }

    public static bool TryParse(string json, out string messageId)
    {
        messageId = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object ||
                !SyncJsonProperties.TryGetUniqueProperty(root, "type", out var typeElement) ||
                typeElement.ValueKind is not JsonValueKind.String ||
                typeElement.GetString() != "ack" ||
                !SyncJsonProperties.TryGetUniqueProperty(root, "messageId", out var messageIdElement) ||
                messageIdElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            var parsedMessageId = messageIdElement.GetString() ?? string.Empty;
            if (!SyncMessageId.IsValid(parsedMessageId))
            {
                return false;
            }

            messageId = parsedMessageId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

#pragma warning disable CA1812 // Instantiated via JSON serialization
    private sealed record AckMessage(string Type, string MessageId);
#pragma warning restore CA1812
}
