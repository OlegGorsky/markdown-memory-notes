using Microsoft.Extensions.Logging;
using Notes.Core.Sync;

namespace Notes.Sync;

public static class SyncPresenceBroadcaster
{
    private static readonly Guid ServerSenderId = Guid.Empty;

    public static Task BroadcastAsync<TConnection>(
        string room,
        SyncRoomRegistry<TConnection> rooms,
        SyncBroadcaster<TConnection> broadcaster,
        TimeSpan sendTimeout,
        ILogger logger)
        where TConnection : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sendTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        var peerCount = rooms.GetPeers(room).Count;
        if (peerCount == 0)
        {
            return Task.CompletedTask;
        }

        return broadcaster.BroadcastAsync(
            room,
            ServerSenderId,
            SyncPresenceMessage.Create(peerCount),
            sendTimeout,
            logger);
    }
}
