using System.Collections.Concurrent;

namespace Notes.Sync;

public enum SyncJoinResult
{
    Joined,
    InvalidRoom,
    RoomLimitReached,
    RoomFull
}

public sealed record SyncRoomStats(int Rooms, int Connections);

public sealed class SyncRoomRegistry<TConnection>
    where TConnection : notnull
{
    private readonly int maxRooms;
    private readonly int maxPeersPerRoom;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TConnection>> rooms = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public SyncRoomRegistry(int maxRooms, int maxPeersPerRoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRooms);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPeersPerRoom);

        this.maxRooms = maxRooms;
        this.maxPeersPerRoom = maxPeersPerRoom;
    }

    public SyncJoinResult TryJoin(string room, Guid connectionId, TConnection connection)
    {
        if (!SyncRoomCode.IsValid(room))
        {
            return SyncJoinResult.InvalidRoom;
        }

        lock (gate)
        {
            if (!rooms.TryGetValue(room, out var peers))
            {
                if (rooms.Count >= maxRooms)
                {
                    return SyncJoinResult.RoomLimitReached;
                }

                peers = new ConcurrentDictionary<Guid, TConnection>();
                rooms[room] = peers;
            }

            if (!peers.ContainsKey(connectionId) && peers.Count >= maxPeersPerRoom)
            {
                return SyncJoinResult.RoomFull;
            }

            peers[connectionId] = connection;
            return SyncJoinResult.Joined;
        }
    }

    public IReadOnlyList<KeyValuePair<Guid, TConnection>> GetPeers(string room)
    {
        return rooms.TryGetValue(room, out var peers)
            ? peers.ToArray()
            : Array.Empty<KeyValuePair<Guid, TConnection>>();
    }

    public IReadOnlyList<string> GetRooms()
    {
        return rooms.Keys.ToArray();
    }

    public bool Contains(string room, Guid connectionId)
    {
        return rooms.TryGetValue(room, out var peers) && peers.ContainsKey(connectionId);
    }

    public bool Leave(string room, Guid connectionId)
    {
        lock (gate)
        {
            if (!rooms.TryGetValue(room, out var peers))
            {
                return false;
            }

            var removed = peers.TryRemove(connectionId, out _);
            if (peers.IsEmpty)
            {
                rooms.TryRemove(room, out _);
            }

            return removed;
        }
    }

    public SyncRoomStats Stats
    {
        get
        {
            var roomCount = 0;
            var connectionCount = 0;
            foreach (var room in rooms.Values)
            {
                roomCount++;
                connectionCount += room.Count;
            }

            return new SyncRoomStats(roomCount, connectionCount);
        }
    }
}
