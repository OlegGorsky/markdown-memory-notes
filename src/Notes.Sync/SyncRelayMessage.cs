using System.Text;
using System.Text.Json;
using Notes.Core.Files;
using Notes.Core.Sync;

namespace Notes.Sync;

public static class SyncRelayMessage
{
    public static bool IsHeartbeat(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object ||
                HasDuplicateProtocolProperty(document.RootElement) ||
                !TryGetSingleProperty(document.RootElement, "type", out var typeElement) ||
                typeElement.ValueKind is not JsonValueKind.String ||
                typeElement.GetString() != "heartbeat")
            {
                return false;
            }

            var propertyCount = 0;
            foreach (var _ in document.RootElement.EnumerateObject())
            {
                propertyCount++;
            }

            return propertyCount == 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsValid(string json, int maxContentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxContentBytes);

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
            if (type == "repairRequest")
            {
                return SyncRepairRequestMessage.IsValid(json, maxContentBytes);
            }

            if (!TryGetSingleProperty(document.RootElement, "path", out var pathElement) ||
                pathElement.ValueKind is not JsonValueKind.String ||
                !VaultRelativePath.TryNormalizeMarkdownContentPath(pathElement.GetString() ?? string.Empty, out _))
            {
                return false;
            }

            if (!HasValidOptionalBaseHash(document.RootElement) ||
                !HasValidOptionalMessageId(document.RootElement))
            {
                return false;
            }

            if (type == "delete")
            {
                return !TryGetSingleProperty(document.RootElement, "content", out var deleteContentElement) ||
                       deleteContentElement.ValueKind is JsonValueKind.Null;
            }

            if (type != "file" ||
                !TryGetSingleProperty(document.RootElement, "content", out var contentElement) ||
                contentElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            var content = contentElement.GetString() ?? string.Empty;
            return Encoding.UTF8.GetByteCount(content) <= maxContentBytes;
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

    private static bool HasValidOptionalMessageId(JsonElement element)
    {
        if (!TryGetSingleProperty(element, "messageId", out var messageIdElement) ||
            messageIdElement.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        return messageIdElement.ValueKind is JsonValueKind.String &&
               SyncMessageId.IsValid(messageIdElement.GetString());
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
