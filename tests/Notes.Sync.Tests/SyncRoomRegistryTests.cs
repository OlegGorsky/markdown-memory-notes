using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRoomRegistryTests
{
    [Fact]
    public void TryJoinRejectsNewRoomWhenRoomLimitIsReached()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 1, maxPeersPerRoom: 2);

        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin("ROOM1", Guid.NewGuid(), "peer-a"));

        Assert.Equal(SyncJoinResult.RoomLimitReached, registry.TryJoin("ROOM2", Guid.NewGuid(), "peer-b"));
    }

    [Fact]
    public void TryJoinRejectsPeerWhenRoomIsFull()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 2, maxPeersPerRoom: 1);

        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin("ROOM1", Guid.NewGuid(), "peer-a"));

        Assert.Equal(SyncJoinResult.RoomFull, registry.TryJoin("ROOM1", Guid.NewGuid(), "peer-b"));
    }

    [Fact]
    public void LeaveRemovesEmptyRoomsFromStats()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 2, maxPeersPerRoom: 2);
        var id = Guid.NewGuid();
        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin("ROOM1", id, "peer-a"));

        registry.Leave("ROOM1", id);

        var stats = registry.Stats;
        Assert.Equal(0, stats.Rooms);
        Assert.Equal(0, stats.Connections);
    }
}
