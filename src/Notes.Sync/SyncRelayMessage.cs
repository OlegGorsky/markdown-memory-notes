using System.Text;
using System.Text.Json;
using Notes.Core.Files;

namespace Notes.Sync;

public static class SyncRelayMessage
{
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

            if (!TryGetProperty(document.RootElement, "type", out var typeElement) ||
                typeElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            if (!TryGetProperty(document.RootElement, "path", out var pathElement) ||
                pathElement.ValueKind is not JsonValueKind.String ||
                !VaultRelativePath.TryNormalizeMarkdownContentPath(pathElement.GetString() ?? string.Empty, out _))
            {
                return false;
            }

            var type = typeElement.GetString();
            if (type == "delete")
            {
                return true;
            }

            if (type != "file" ||
                !TryGetProperty(document.RootElement, "content", out var contentElement) ||
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

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.NameEquals(name) ||
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
