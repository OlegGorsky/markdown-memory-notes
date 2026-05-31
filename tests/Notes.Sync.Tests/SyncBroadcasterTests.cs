using Microsoft.Extensions.Logging.Abstractions;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBroadcasterTests
{
    private const string Room = "RoomFanout-ABCDEFGHijkl";

    [Fact]
    public async Task BroadcastAsyncSendsToPeersExceptSender()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var senderId = Guid.NewGuid();
        var sender = new TestPeer();
        var peer = new TestPeer();
        registry.TryJoin(Room, senderId, sender);
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        var metrics = new SyncMetrics();

        var broadcaster = new SyncBroadcaster<TestPeer>(registry, static peer => peer.IsOpen, SendAsync, maxFanoutConcurrency: 4, metrics);

        var result = await broadcaster.BroadcastAsync(Room, senderId, "payload", TimeSpan.FromSeconds(1), NullLogger.Instance);

        Assert.Empty(sender.Messages);
        Assert.Equal(["payload"], peer.Messages);
        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, metrics.Snapshot().DeliveriesAttempted);
        Assert.Equal(1, metrics.Snapshot().DeliveriesSucceeded);
    }

    [Fact]
    public async Task BroadcastAsyncRemovesClosedPeers()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var senderId = Guid.NewGuid();
        var closedPeerId = Guid.NewGuid();
        registry.TryJoin(Room, senderId, new TestPeer());
        registry.TryJoin(Room, closedPeerId, new TestPeer { IsOpen = false });
        var metrics = new SyncMetrics();

        var broadcaster = new SyncBroadcaster<TestPeer>(registry, static peer => peer.IsOpen, SendAsync, maxFanoutConcurrency: 4, metrics);

        var result = await broadcaster.BroadcastAsync(Room, senderId, "payload", TimeSpan.FromSeconds(1), NullLogger.Instance);

        Assert.DoesNotContain(registry.GetPeers(Room), peer => peer.Key == closedPeerId);
        Assert.Equal(1, result.Attempted);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, metrics.Snapshot().DeliveriesFailed);
        Assert.Equal(1, metrics.Snapshot().PeersRemoved);
    }

    [Fact]
    public async Task BroadcastAsyncRemovesPeerWhenSendFailsAfterOpenCheck()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var senderId = Guid.NewGuid();
        var failedPeerId = Guid.NewGuid();
        registry.TryJoin(Room, senderId, new TestPeer());
        registry.TryJoin(Room, failedPeerId, new TestPeer());
        var metrics = new SyncMetrics();
        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            static (_, _, _) => throw new InvalidOperationException("Socket is no longer writable."),
            maxFanoutConcurrency: 4,
            metrics);

        var result = await broadcaster.BroadcastAsync(Room, senderId, "payload", TimeSpan.FromSeconds(1), NullLogger.Instance);

        Assert.DoesNotContain(registry.GetPeers(Room), peer => peer.Key == failedPeerId);
        Assert.Equal(1, result.Attempted);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, metrics.Snapshot().DeliveriesFailed);
        Assert.Equal(1, metrics.Snapshot().PeersRemoved);
    }

    [Fact]
    public async Task BroadcastAsyncBoundsConcurrentSends()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 8);
        var senderId = Guid.NewGuid();
        registry.TryJoin(Room, senderId, new TestPeer());
        for (var index = 0; index < 5; index++)
        {
            registry.TryJoin(Room, Guid.NewGuid(), new TestPeer());
        }

        var currentSends = 0;
        var maxObservedSends = 0;
        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            async (peer, payload, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref currentSends);
                UpdateMax(ref maxObservedSends, current);
                try
                {
                    await Task.Delay(40, cancellationToken);
                    peer.Messages.Add("sent");
                }
                finally
                {
                    Interlocked.Decrement(ref currentSends);
                }
            },
            maxFanoutConcurrency: 2,
            new SyncMetrics());

        await broadcaster.BroadcastAsync(Room, senderId, "payload", TimeSpan.FromSeconds(1), NullLogger.Instance);

        Assert.Equal(2, maxObservedSends);
        Assert.Equal(5, registry.GetPeers(Room).Count(peer => peer.Value.Messages.Count == 1));
    }

    [Fact]
    public async Task BroadcastAsyncSerializesConcurrentSendsToSamePeer()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var senderId = Guid.NewGuid();
        var sharedPeer = new TestPeer();
        var firstSendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.TryJoin(Room, senderId, new TestPeer());
        registry.TryJoin(Room, Guid.NewGuid(), sharedPeer);
        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            async (peer, payload, cancellationToken) =>
            {
                peer.EnterSend();
                try
                {
                    if (ReferenceEquals(peer, sharedPeer))
                    {
                        firstSendEntered.TrySetResult();
                        await Task.Delay(120, cancellationToken);
                    }

                    peer.Messages.Add(payload);
                }
                finally
                {
                    peer.LeaveSend();
                }
            },
            maxFanoutConcurrency: 4,
            new SyncMetrics());

        var firstBroadcast = broadcaster.BroadcastAsync(
            Room,
            senderId,
            "first",
            TimeSpan.FromSeconds(2),
            NullLogger.Instance);
        await firstSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var secondBroadcast = broadcaster.BroadcastAsync(
            Room,
            Guid.NewGuid(),
            "second",
            TimeSpan.FromSeconds(2),
            NullLogger.Instance);

        await Task.WhenAll(firstBroadcast, secondBroadcast);

        Assert.Equal(1, sharedPeer.MaxObservedSends);
        Assert.Equal(["first", "second"], sharedPeer.Messages);
    }

    private static Task SendAsync(TestPeer peer, string payload, CancellationToken cancellationToken)
    {
        peer.Messages.Add(payload);
        return Task.CompletedTask;
    }

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current ||
                Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private sealed class TestPeer
    {
        private int activeSends;
        private int maxObservedSends;

        public bool IsOpen { get; init; } = true;
        public List<string> Messages { get; } = new();
        public int MaxObservedSends => Volatile.Read(ref maxObservedSends);

        public void EnterSend()
        {
            var current = Interlocked.Increment(ref activeSends);
            UpdateMax(ref maxObservedSends, current);
        }

        public void LeaveSend()
        {
            Interlocked.Decrement(ref activeSends);
        }
    }
}
