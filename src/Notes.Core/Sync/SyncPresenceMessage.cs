using System.Text.Json;

namespace Notes.Core.Sync;

public static class SyncPresenceMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(int peerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peerCount);
        return JsonSerializer.Serialize(new PresenceMessage("presence", peerCount), JsonOptions);
    }

    public static bool TryParse(string json, out int peerCount)
    {
        peerCount = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object ||
                !SyncJsonProperties.TryGetUniqueProperty(root, "type", out var typeElement) ||
                typeElement.ValueKind is not JsonValueKind.String ||
                typeElement.GetString() != "presence" ||
                !SyncJsonProperties.TryGetUniqueProperty(root, "peerCount", out var peerCountElement) ||
                peerCountElement.ValueKind is not JsonValueKind.Number ||
                !peerCountElement.TryGetInt32(out var parsedPeerCount) ||
                parsedPeerCount <= 0)
            {
                return false;
            }

            peerCount = parsedPeerCount;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

#pragma warning disable CA1812 // Instantiated via JSON deserialization
    private sealed record PresenceMessage(string Type, int PeerCount);
#pragma warning restore CA1812
}
