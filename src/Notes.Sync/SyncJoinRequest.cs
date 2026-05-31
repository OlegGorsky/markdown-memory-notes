using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Notes.Sync;

public static class SyncJoinRequest
{
    public static bool TryGetRoom(string json, [NotNullWhen(true)] out string? room)
    {
        room = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object ||
                !TryGetSingleProperty(document.RootElement, "room", out var roomElement) ||
                roomElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            var requestedRoom = roomElement.GetString() ?? string.Empty;
            if (!SyncRoomCode.IsValid(requestedRoom))
            {
                return false;
            }

            room = requestedRoom;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
