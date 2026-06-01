using System.Text;
using System.Text.Json;
using Notes.Core.Files;
using Notes.Core.Sync;

namespace Notes.Sync;

public enum SyncRelayMessageKind
{
    Invalid,
    Heartbeat,
    Relay
}

public readonly record struct SyncRelayMessageClassification(
    SyncRelayMessageKind Kind,
    string? MessageId);

public static class SyncRelayMessage
{
    public static bool IsHeartbeat(string json)
    {
        return TryClassify(json, int.MaxValue, out var classification) &&
               classification.Kind is SyncRelayMessageKind.Heartbeat;
    }

    public static bool IsValid(string json, int maxContentBytes)
    {
        return TryClassify(json, maxContentBytes, out var classification) &&
               classification.Kind is SyncRelayMessageKind.Relay;
    }

    public static bool TryClassify(
        string json,
        int maxContentBytes,
        out SyncRelayMessageClassification classification)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxContentBytes);
        classification = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            if (HasDuplicateProtocolProperty(document.RootElement))
            {
                return false;
            }

            if (!TryGetSingleProperty(document.RootElement, "type", out var typeElement) ||
                typeElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString();
            if (type == "heartbeat")
            {
                if (!HasSingleProperty(document.RootElement))
                {
                    return false;
                }

                classification = new SyncRelayMessageClassification(
                    SyncRelayMessageKind.Heartbeat,
                    MessageId: null);
                return true;
            }

            if (type == "repairRequest")
            {
                if (!SyncRepairRequestMessage.IsValid(json, maxContentBytes) ||
                    !TryGetOptionalMessageId(document.RootElement, out var repairMessageId))
                {
                    return false;
                }

                classification = new SyncRelayMessageClassification(
                    SyncRelayMessageKind.Relay,
                    repairMessageId);
                return true;
            }

            if (!TryGetSingleProperty(document.RootElement, "path", out var pathElement) ||
                pathElement.ValueKind is not JsonValueKind.String ||
                !VaultRelativePath.TryNormalizeMarkdownContentPath(pathElement.GetString() ?? string.Empty, out _))
            {
                return false;
            }

            if (!HasValidOptionalBaseHash(document.RootElement) ||
                !TryGetOptionalMessageId(document.RootElement, out var messageId))
            {
                return false;
            }

            if (type == "delete")
            {
                if (TryGetSingleProperty(document.RootElement, "content", out var deleteContentElement) &&
                    deleteContentElement.ValueKind is not JsonValueKind.Null)
                {
                    return false;
                }

                classification = new SyncRelayMessageClassification(
                    SyncRelayMessageKind.Relay,
                    messageId);
                return true;
            }

            if (type != "file" ||
                !TryGetSingleProperty(document.RootElement, "content", out var contentElement) ||
                contentElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            var content = contentElement.GetString() ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(content) > maxContentBytes)
            {
                return false;
            }

            classification = new SyncRelayMessageClassification(
                SyncRelayMessageKind.Relay,
                messageId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetMessageId(string json, out string messageId)
    {
        messageId = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object ||
                !TryGetSingleProperty(document.RootElement, "messageId", out var messageIdElement) ||
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

    private static bool HasValidOptionalBaseHash(JsonElement element)
    {
        if (!TryGetSingleProperty(element, "baseHash", out var baseHashElement) ||
            baseHashElement.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        return baseHashElement.ValueKind is JsonValueKind.String &&
               SyncContentHash.IsValid(baseHashElement.GetString());
    }

    private static bool TryGetOptionalMessageId(JsonElement element, out string? messageId)
    {
        messageId = null;
        if (!TryGetSingleProperty(element, "messageId", out var messageIdElement) ||
            messageIdElement.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        if (messageIdElement.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        var parsedMessageId = messageIdElement.GetString();
        if (!SyncMessageId.IsValid(parsedMessageId))
        {
            return false;
        }

        messageId = parsedMessageId;
        return true;
    }

    private static bool HasSingleProperty(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject())
        {
            count++;
            if (count > 1)
            {
                return false;
            }
        }

        return count == 1;
    }

    private static bool HasDuplicateProtocolProperty(JsonElement element)
    {
        return HasDuplicateProperty(element, "type") ||
               HasDuplicateProperty(element, "path") ||
               HasDuplicateProperty(element, "content") ||
               HasDuplicateProperty(element, "baseHash") ||
               HasDuplicateProperty(element, "messageId") ||
               HasDuplicateProperty(element, "entries") ||
               HasDuplicateProperty(element, "truncated");
    }

    private static bool HasDuplicateProperty(JsonElement element, string name)
    {
        var count = 0;
        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.NameEquals(name) ||
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                if (count > 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetSingleProperty(JsonElement element, string name, out JsonElement property)
    {
        var found = false;
        property = default;
        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.NameEquals(name) ||
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (found)
                {
                    property = default;
                    return false;
                }

                property = candidate.Value;
                found = true;
            }
        }

        return found;
    }
}
