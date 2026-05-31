using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRoomRegistryTests
{
    private const string RoomOne = "RoomOne-ABCDEFGHijklMN";
    private const string RoomTwo = "RoomTwo-ABCDEFGHijklMN";

    [Fact]
    public void TryJoinRejectsNewRoomWhenRoomLimitIsReached()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 1, maxPeersPerRoom: 2);

        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin(RoomOne, Guid.NewGuid(), "peer-a"));

        Assert.Equal(SyncJoinResult.RoomLimitReached, registry.TryJoin(RoomTwo, Guid.NewGuid(), "peer-b"));
    }

    [Fact]
    public void TryJoinRejectsPeerWhenRoomIsFull()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 2, maxPeersPerRoom: 1);

        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin(RoomOne, Guid.NewGuid(), "peer-a"));

        Assert.Equal(SyncJoinResult.RoomFull, registry.TryJoin(RoomOne, Guid.NewGuid(), "peer-b"));
    }

    [Fact]
    public void TryJoinReplacesExistingConnectionWhenRoomIsFull()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 1, maxPeersPerRoom: 1);
        var connectionId = Guid.NewGuid();

        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin(RoomOne, connectionId, "peer-a"));
        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin(RoomOne, connectionId, "peer-a-reconnected"));

        var peer = Assert.Single(registry.GetPeers(RoomOne));
        Assert.Equal(connectionId, peer.Key);
        Assert.Equal("peer-a-reconnected", peer.Value);
        Assert.Equal(new SyncRoomStats(Rooms: 1, Connections: 1), registry.Stats);
    }

    [Fact]
    public void LeaveRemovesEmptyRoomsFromStats()
    {
        var registry = new SyncRoomRegistry<string>(maxRooms: 2, maxPeersPerRoom: 2);
        var id = Guid.NewGuid();
        Assert.Equal(SyncJoinResult.Joined, registry.TryJoin(RoomOne, id, "peer-a"));

        registry.Leave(RoomOne, id);

        var stats = registry.Stats;
        Assert.Equal(0, stats.Rooms);
        Assert.Equal(0, stats.Connections);
    }
}
