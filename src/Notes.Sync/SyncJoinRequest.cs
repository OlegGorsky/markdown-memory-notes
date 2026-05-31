using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Notes.Sync;

public static class SyncJoinRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryGetRoom(string json, [NotNullWhen(true)] out string? room)
    {
        room = null;
        try
        {
            var join = JsonSerializer.Deserialize<JoinMessage>(json, JsonOptions);
            if (join?.Room is not { } requestedRoom ||
                !SyncRoomCode.IsValid(requestedRoom))
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

#pragma warning disable CA1812 // Instantiated via JSON deserialization
    private sealed record JoinMessage(string Room);
#pragma warning restore CA1812
}
