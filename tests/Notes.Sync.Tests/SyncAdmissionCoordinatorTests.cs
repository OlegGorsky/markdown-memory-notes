using Microsoft.Extensions.Logging.Abstractions;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncAdmissionCoordinatorTests
{
    private const string Room = "RoomAdmission-ABCDEFGH";
    private const int MaxRooms = 3;
    private const int MaxPeersPerRoom = 2;

    [Fact]
    public async Task TryJoinAsyncReleasesLocalPeerWhenDistributedRoomIsFull()
    {
        var registry = new SyncRoomRegistry<string>(MaxRooms, MaxPeersPerRoom);
        var admission = new FakeAdmissionController { JoinResult = SyncJoinResult.RoomFull };
        var metrics = new SyncMetrics();
        var coordinator = CreateCoordinator(registry, admission, metrics);
        var connectionId = Guid.NewGuid();

        var result = await coordinator.TryJoinAsync(Room, connectionId, "peer-a", CancellationToken.None);

        Assert.Equal(SyncJoinResult.RoomFull, result);
        Assert.Equal(new SyncRoomStats(Rooms: 0, Connections: 0), registry.Stats);
        Assert.Equal((Room, connectionId, MaxRooms, MaxPeersPerRoom), admission.Joined);
        Assert.Equal(1, metrics.Snapshot().AdmissionRejectedRoomFull);
    }

    [Fact]
    public async Task TryJoinAsyncFallsBackToLocalJoinWhenAdmissionControllerFails()
    {
        var registry = new SyncRoomRegistry<string>(MaxRooms, MaxPeersPerRoom);
        var admission = new ThrowingAdmissionController();
        var metrics = new SyncMetrics();
        var coordinator = CreateCoordinator(registry, admission, metrics);
        var connectionId = Guid.NewGuid();

        var result = await coordinator.TryJoinAsync(Room, connectionId, "peer-a", CancellationToken.None);

        Assert.Equal(SyncJoinResult.Joined, result);
        Assert.Equal(new SyncRoomStats(Rooms: 1, Connections: 1), registry.Stats);
        Assert.Equal(1, metrics.Snapshot().AdmissionControllerFailed);
    }

    [Fact]
    public async Task PeerLeftAsyncDoesNotReleaseDistributedAdmissionAfterFallbackJoin()
    {
        var registry = new SyncRoomRegistry<string>(MaxRooms, MaxPeersPerRoom);
        var admission = new ThrowingAdmissionController();
        var metrics = new SyncMetrics();
        var coordinator = CreateCoordinator(registry, admission, metrics);
        var connectionId = Guid.NewGuid();
        Assert.Equal(
            SyncJoinResult.Joined,
            await coordinator.TryJoinAsync(Room, connectionId, "peer-a", CancellationToken.None));

        await coordinator.PeerLeftAsync(Room, connectionId, CancellationToken.None);

        Assert.Equal(new SyncRoomStats(Rooms: 0, Connections: 0), registry.Stats);
        Assert.Equal(1, admission.JoinAttempts);
        Assert.Equal(0, admission.LeaveAttempts);
        Assert.Equal(1, metrics.Snapshot().AdmissionControllerFailed);
    }

    [Fact]
    public async Task PeerLeftAsyncRemovesLocalPeerAndReleasesDistributedAdmission()
    {
        var registry = new SyncRoomRegistry<string>(MaxRooms, MaxPeersPerRoom);
        var admission = new FakeAdmissionController { JoinResult = SyncJoinResult.Joined };
        var metrics = new SyncMetrics();
        var coordinator = CreateCoordinator(registry, admission, metrics);
        var connectionId = Guid.NewGuid();
        Assert.Equal(
            SyncJoinResult.Joined,
            await coordinator.TryJoinAsync(Room, connectionId, "peer-a", CancellationToken.None));

        await coordinator.PeerLeftAsync(Room, connectionId, CancellationToken.None);

        Assert.Equal(new SyncRoomStats(Rooms: 0, Connections: 0), registry.Stats);
        Assert.Equal((Room, connectionId), admission.Left);
    }

    private static SyncAdmissionCoordinator<string> CreateCoordinator(
        SyncRoomRegistry<string> registry,
        ISyncAdmissionController admission,
        SyncMetrics metrics)
    {
        return new SyncAdmissionCoordinator<string>(
            registry,
            admission,
            MaxRooms,
            MaxPeersPerRoom,
            metrics,
            NullLogger.Instance);
    }

    private sealed class FakeAdmissionController : ISyncAdmissionController
    {
        public SyncJoinResult JoinResult { get; init; } = SyncJoinResult.Joined;
        public bool IsDistributed => true;
        public (string Room, Guid ConnectionId, int MaxRooms, int MaxPeersPerRoom)? Joined { get; private set; }
        public (string Room, Guid ConnectionId)? Left { get; private set; }

        public Task<SyncJoinResult> TryJoinAsync(
            string room,
            Guid connectionId,
            int maxRooms,
            int maxPeersPerRoom,
            CancellationToken cancellationToken)
        {
            Joined = (room, connectionId, maxRooms, maxPeersPerRoom);
            return Task.FromResult(JoinResult);
        }

        public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            Left = (room, connectionId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAdmissionController : ISyncAdmissionController
    {
        public bool IsDistributed => true;
        public int JoinAttempts { get; private set; }
        public int LeaveAttempts { get; private set; }

        public Task<SyncJoinResult> TryJoinAsync(
            string room,
            Guid connectionId,
            int maxRooms,
            int maxPeersPerRoom,
            CancellationToken cancellationToken)
        {
            JoinAttempts++;
            throw new InvalidOperationException("Admission unavailable.");
        }

        public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            LeaveAttempts++;
            throw new InvalidOperationException("Admission unavailable.");
        }
    }
}
