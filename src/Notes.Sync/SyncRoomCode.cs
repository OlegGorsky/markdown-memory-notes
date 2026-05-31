using Notes.Core.Sync;

namespace Notes.Sync;

public static class SyncRoomCode
{
    public static bool IsValid(string? room)
    {
        return SyncPairingCode.IsValid(room);
    }
}
