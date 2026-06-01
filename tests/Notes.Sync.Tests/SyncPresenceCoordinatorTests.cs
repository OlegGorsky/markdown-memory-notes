using Microsoft.Extensions.Logging.Abstractions;
using Notes.Core.Sync;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncPresenceCoordinatorTests
{
    private const string Room = "RoomPresenceDistributed-ABCDEFGH";

    [Fact]
    public async Task PeerJoinedAsyncUsesDistributedPeerCountForLocalAndBackplanePresence()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        var connectionId = Guid.NewGuid();
        registry.TryJoin(Room, connectionId, peer);
        await using var backplane = new RecordingBackplane();
        var tracker = new FakePresenceTracker { PeerCount = 5 };
        using var coordinator = CreateCoordinator(registry, backplane, tracker);

        await coordinator.PeerJoinedAsync(Room, connectionId, CancellationToken.None);

        Assert.Equal((Room, connectionId), tracker.Joined);
        AssertPresenceCount(Assert.Single(peer.Messages), 5);
        var published = Assert.Single(backplane.Published);
        AssertPresenceCount(published.Message.Payload, 5);
    }

    [Fact]
    public async Task PeerLeftAsyncPublishesDistributedPresenceWhenLocalRoomIsEmpty()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var connectionId = Guid.NewGuid();
        await using var backplane = new RecordingBackplane();
        var tracker = new FakePresenceTracker { PeerCount = 3 };
        using var coordinator = CreateCoordinator(registry, backplane, tracker);

        await coordinator.PeerLeftAsync(Room, connectionId, CancellationToken.None);

        Assert.Equal((Room, connectionId), tracker.Left);
        var published = Assert.Single(backplane.Published);
        AssertPresenceCount(published.Message.Payload, 3);
    }

    [Fact]
    public async Task PeerJoinedAsyncFallsBackToLocalPeerCountWhenPresenceTrackerFails()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var firstPeer = new TestPeer();
        var secondPeer = new TestPeer();
        var connectionId = Guid.NewGuid();
        registry.TryJoin(Room, connectionId, firstPeer);
        registry.TryJoin(Room, Guid.NewGuid(), secondPeer);
        await using var backplane = new RecordingBackplane();
        var tracker = new ThrowingPresenceTracker();
        var metrics = new SyncMetrics();
        using var coordinator = CreateCoordinator(registry, backplane, tracker, metrics);

        await coordinator.PeerJoinedAsync(Room, connectionId, CancellationToken.None);

        AssertPresenceCount(Assert.Single(firstPeer.Messages), 2);
        AssertPresenceCount(Assert.Single(secondPeer.Messages), 2);
        var published = Assert.Single(backplane.Published);
        AssertPresenceCount(published.Message.Payload, 2);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.PresenceTrackerJoinFailed);
        Assert.Equal(1, snapshot.PresenceTrackerCountFailed);
    }

    [Fact]
    public async Task PeerJoinedAsyncFallsBackToLocalPeerCountWhenPresenceTrackerTimesOut()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        var connectionId = Guid.NewGuid();
        registry.TryJoin(Room, connectionId, peer);
        await using var backplane = new RecordingBackplane();
        var tracker = new HangingPresenceTracker();
        var metrics = new SyncMetrics();
        using var coordinator = CreateCoordinator(
            registry,
            backplane,
            tracker,
            metrics,
            sendTimeout: TimeSpan.FromMilliseconds(20));

        await coordinator.PeerJoinedAsync(Room, connectionId, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        AssertPresenceCount(Assert.Single(peer.Messages), 1);
        var published = Assert.Single(backplane.Published);
        AssertPresenceCount(published.Message.Payload, 1);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, tracker.JoinAttempts);
        Assert.Equal(1, tracker.CountAttempts);
        Assert.Equal(1, snapshot.PresenceTrackerJoinFailed);
        Assert.Equal(1, snapshot.PresenceTrackerCountFailed);
    }

    private static SyncPresenceCoordinator<TestPeer> CreateCoordinator(
        SyncRoomRegistry<TestPeer> registry,
        ISyncBackplane backplane,
        ISyncPresenceTracker tracker,
        SyncMetrics? metrics = null,
        TimeSpan? sendTimeout = null)
    {
        metrics ??= new SyncMetrics();
        var timeout = sendTimeout ?? TimeSpan.FromSeconds(1);
        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            static (peer, payload, _) =>
            {
                peer.Messages.Add(payload);
                return Task.CompletedTask;
            },
            maxFanoutConcurrency: 4,
            metrics);
#pragma warning disable CA2000 // SyncPresenceCoordinator owns and disposes the bridge.
        var backplaneBridge = new SyncBackplaneBridge<TestPeer>(
            "instance-a",
            registry,
            broadcaster,
            backplane,
            maxMessageBytes: 1024,
            timeout,
            metrics,
            NullLogger.Instance);
#pragma warning restore CA2000

        return new SyncPresenceCoordinator<TestPeer>(
            registry,
            broadcaster,
            backplaneBridge,
            tracker,
            timeout,
            metrics,
            NullLogger.Instance);
    }

    private static void AssertPresenceCount(string payload, int expectedPeerCount)
    {
        Assert.True(SyncPresenceMessage.TryParse(payload, out var peerCount));
        Assert.Equal(expectedPeerCount, peerCount);
    }

    private sealed class TestPeer
    {
        public bool IsOpen { get; init; } = true;
        public List<string> Messages { get; } = new();
    }

    private sealed class FakePresenceTracker : ISyncPresenceTracker
    {
        public int? PeerCount { get; init; }
        public (string Room, Guid ConnectionId)? Joined { get; private set; }
        public (string Room, Guid ConnectionId)? Left { get; private set; }

        public bool IsDistributed => true;

        public Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            Joined = (room, connectionId);
            return Task.CompletedTask;
        }

        public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            Left = (room, connectionId);
            return Task.CompletedTask;
        }

        public Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
        {
            return Task.FromResult(PeerCount);
        }
    }

    private sealed class HangingPresenceTracker : ISyncPresenceTracker
    {
        public bool IsDistributed => true;
        public int JoinAttempts { get; private set; }
        public int CountAttempts { get; private set; }

        public async Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            JoinAttempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
        {
            CountAttempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class ThrowingPresenceTracker : ISyncPresenceTracker
    {
        public bool IsDistributed => true;

        public Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Presence tracker failed.");
        }

        public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Presence tracker failed.");
        }

        public Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Presence tracker failed.");
        }
    }

    private sealed class RecordingBackplane : ISyncBackplane
    {
        public bool IsEnabled => true;
        public List<(string Room, SyncBackplaneMessage Message)> Published { get; } = new();

        public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(1)));
        }

        public Task<IDisposable> SubscribeAsync(
            string room,
            Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IDisposable>(new DisposableAction());
        }

        public Task<SyncBackplanePublishResult> PublishAsync(
            string room,
            SyncBackplaneMessage message,
            CancellationToken cancellationToken)
        {
            Published.Add((room, message));
            return Task.FromResult(new SyncBackplanePublishResult(Published: true, RemoteSubscribers: 1));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposableAction : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
