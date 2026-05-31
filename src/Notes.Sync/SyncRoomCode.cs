using System.Text.RegularExpressions;

namespace Notes.Sync;

public static partial class SyncRoomCode
{
    public static bool IsValid(string? room)
    {
        return room is not null && RoomCodeRegex().IsMatch(room);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{4,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoomCodeRegex();
}
