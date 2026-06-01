using Microsoft.Extensions.Logging.Abstractions;
using Notes.Core.Sync;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBackplaneBridgeTests
{
    private const string Room = "RoomBackplane-ABCDEFGH";
    private const string RelayPayload = """{"type":"file","path":"notes/a.md","content":"# A"}""";
    private const string DeletePayload = """{"type":"delete","path":"notes/a.md"}""";

    [Fact]
    public async Task ReceiveAsyncBroadcastsRemoteBackplaneMessagesToLocalPeers()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, metrics: metrics);

        await bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-b", Guid.NewGuid(), RelayPayload),
            CancellationToken.None);

        Assert.Equal([RelayPayload], peer.Messages);
        Assert.Equal(1, metrics.Snapshot().BackplaneMessagesReceived);
    }

    [Fact]
    public async Task ReceiveAsyncBroadcastsRemotePresenceMessagesToLocalPeers()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, metrics: metrics);
        var presencePayload = SyncPresenceMessage.Create(peerCount: 2);

        await bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-b", Guid.Empty, presencePayload),
            CancellationToken.None);

        Assert.Equal([presencePayload], peer.Messages);
        Assert.Equal(1, metrics.Snapshot().BackplaneMessagesReceived);
    }

    [Fact]
    public async Task ReceiveAsyncIgnoresMessagesFromSameInstance()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, metrics: metrics);

        await bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-a", Guid.NewGuid(), "payload"),
            CancellationToken.None);

        Assert.Empty(peer.Messages);
        Assert.Equal(1, metrics.Snapshot().BackplaneMessagesIgnored);
    }

    [Fact]
    public async Task ReceiveAsyncRejectsInvalidRemotePayloads()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, metrics: metrics);

        var result = await bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage(
                "instance-b",
                Guid.NewGuid(),
                """{"type":"ack","messageId":"0123456789abcdef0123456789abcdef"}"""),
            CancellationToken.None);

        Assert.Empty(peer.Messages);
        Assert.Equal(0, result.Attempted);
        Assert.Equal(1, metrics.Snapshot().BackplaneInvalidPayload);
    }

    [Fact]
    public async Task ReceiveAsyncSerializesRemoteBroadcastsForSameRoom()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var slowPeer = new TestPeer();
        var fastPeer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), slowPeer);
        registry.TryJoin(Room, Guid.NewGuid(), fastPeer);
        var firstSlowSendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSlowSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPayloadSendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bridge = CreateBridge(
            "instance-a",
            registry,
            sendAsync: async (peer, payload, cancellationToken) =>
            {
                if (payload == RelayPayload && ReferenceEquals(peer, slowPeer))
                {
                    firstSlowSendEntered.SetResult();
                    await releaseFirstSlowSend.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
                }
                else if (payload == DeletePayload)
                {
                    secondPayloadSendStarted.TrySetResult();
                }

                peer.Messages.Add(payload);
            });

        var firstReceive = bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-b", Guid.NewGuid(), RelayPayload),
            CancellationToken.None);
        await firstSlowSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var secondReceive = bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-b", Guid.NewGuid(), DeletePayload),
            CancellationToken.None);
        var secondStartedBeforeFirstCompleted = await Task.WhenAny(
            secondPayloadSendStarted.Task,
            Task.Delay(100, TestContext.Current.CancellationToken)) == secondPayloadSendStarted.Task;
        releaseFirstSlowSend.SetResult();

        await Task.WhenAll(firstReceive, secondReceive);

        Assert.False(secondStartedBeforeFirstCompleted);
        Assert.Equal(0, bridge.ReceiveGateCount);
    }

    [Fact]
    public async Task PublishAsyncPublishesLocalMessagesToBackplane()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new FakeSyncBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics);
        var senderId = Guid.NewGuid();

        var result = await bridge.PublishAsync(Room, senderId, "payload", CancellationToken.None);

        Assert.True(result.Published);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.BackplanePublishAttempted);
        Assert.Equal(1, snapshot.BackplanePublishSucceeded);
        Assert.Equal(0, snapshot.BackplanePublishFailed);
        Assert.Equal(1, snapshot.BackplaneRemoteSubscribers);
        var published = Assert.Single(backplane.Published);
        Assert.Equal(Room, published.Room);
        Assert.Equal("instance-a", published.Message.OriginInstanceId);
        Assert.Equal(senderId, published.Message.SenderConnectionId);
        Assert.Equal("payload", published.Message.Payload);
    }

    [Fact]
    public async Task PublishAsyncDoesNotFailClientWhenBackplaneFails()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new ThrowingSyncBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics);

        var result = await bridge.PublishAsync(Room, Guid.NewGuid(), "payload", CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(0, result.RemoteSubscribers);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.BackplanePublishAttempted);
        Assert.Equal(0, snapshot.BackplanePublishSucceeded);
        Assert.Equal(1, snapshot.BackplanePublishFailed);
    }

    [Fact]
    public async Task PublishAsyncTimesOutWhenBackplanePublishHangs()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new HangingSyncBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics, sendTimeout: TimeSpan.FromMilliseconds(20));

        var result = await bridge.PublishAsync(Room, Guid.NewGuid(), RelayPayload, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.False(result.Published);
        Assert.Equal(0, result.RemoteSubscribers);
        Assert.Equal(1, metrics.Snapshot().BackplanePublishFailed);
    }

    [Fact]
    public async Task EnsureSubscribedAsyncDoesNotFailClientWhenBackplaneFails()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new ThrowingSyncBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics);

        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);

        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.BackplaneSubscribeAttempted);
        Assert.Equal(0, snapshot.BackplaneSubscribeSucceeded);
        Assert.Equal(1, snapshot.BackplaneSubscribeFailed);
        Assert.Equal(0, bridge.SubscriptionCount);
    }

    [Fact]
    public async Task EnsureSubscribedAsyncTimesOutWhenBackplaneSubscribeHangs()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new HangingSyncBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics, sendTimeout: TimeSpan.FromMilliseconds(20));

        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.BackplaneSubscribeAttempted);
        Assert.Equal(0, snapshot.BackplaneSubscribeSucceeded);
        Assert.Equal(1, snapshot.BackplaneSubscribeFailed);
        Assert.Equal(0, bridge.SubscriptionCount);
    }

    [Fact]
    public async Task EnsureSubscribedAsyncDisposesLateSubscriptionAfterTimeout()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new LateCompletingSubscribeBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics, sendTimeout: TimeSpan.FromMilliseconds(20));
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(0, bridge.SubscriptionCount);
        Assert.Equal(1, metrics.Snapshot().BackplaneSubscribeFailed);

        using var subscription = new DisposableAction(() => disposed.TrySetResult());
        backplane.CompleteSubscribe(subscription);

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(0, bridge.SubscriptionCount);
    }

    [Fact]
    public async Task EnsureSubscribedAsyncSubscribesOnlyOncePerRoom()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new FakeSyncBackplane();
        var metrics = new SyncMetrics();
        using var bridge = CreateBridge("instance-a", registry, backplane, metrics);

        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);
        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);

        Assert.Equal(1, backplane.SubscribeCount);
        Assert.Equal(1, bridge.SubscriptionCount);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.BackplaneSubscribeAttempted);
        Assert.Equal(1, snapshot.BackplaneSubscribeSucceeded);
        Assert.Equal(0, snapshot.BackplaneSubscribeFailed);
    }

    [Fact]
    public async Task ReleaseIfRoomEmptyAsyncDisposesRoomSubscription()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var connectionId = Guid.NewGuid();
        registry.TryJoin(Room, connectionId, new TestPeer());
        await using var backplane = new FakeSyncBackplane();
        using var bridge = CreateBridge("instance-a", registry, backplane);
        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);

        await bridge.ReleaseIfRoomEmptyAsync(Room);

        Assert.Equal(0, backplane.DisposeCount);

        registry.Leave(Room, connectionId);
        await bridge.ReleaseIfRoomEmptyAsync(Room);

        Assert.Equal(1, backplane.DisposeCount);
        Assert.Equal(0, bridge.SubscriptionCount);
    }

    private static SyncBackplaneBridge<TestPeer> CreateBridge(
        string instanceId,
        SyncRoomRegistry<TestPeer> registry,
        ISyncBackplane? backplane = null,
        SyncMetrics? metrics = null,
        Func<TestPeer, string, CancellationToken, Task>? sendAsync = null,
        TimeSpan? sendTimeout = null)
    {
        metrics ??= new SyncMetrics();
        sendAsync ??= static (peer, payload, _) =>
        {
            peer.Messages.Add(payload);
            return Task.CompletedTask;
        };
        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            sendAsync,
            maxFanoutConcurrency: 4,
            metrics);

        return new SyncBackplaneBridge<TestPeer>(
            instanceId,
            registry,
            broadcaster,
            backplane ?? NoopSyncBackplane.Instance,
            maxMessageBytes: 1024,
            sendTimeout ?? TimeSpan.FromSeconds(1),
            metrics,
            NullLogger.Instance);
    }

    private sealed class TestPeer
    {
        public bool IsOpen { get; init; } = true;
        public List<string> Messages { get; } = new();
    }

    private sealed class FakeSyncBackplane : ISyncBackplane
    {
        public bool IsEnabled => true;
        public List<(string Room, SyncBackplaneMessage Message)> Published { get; } = new();
        public int SubscribeCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(1)));
        }

        public Task<IDisposable> SubscribeAsync(
            string room,
            Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            SubscribeCount++;
            return Task.FromResult<IDisposable>(new DisposableAction(() => DisposeCount++));
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

    private sealed class ThrowingSyncBackplane : ISyncBackplane
    {
        public bool IsEnabled => true;

        public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncBackplaneHealth.Unavailable);
        }

        public Task<IDisposable> SubscribeAsync(
            string room,
            Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Backplane unavailable.");
        }

        public Task<SyncBackplanePublishResult> PublishAsync(
            string room,
            SyncBackplaneMessage message,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Backplane unavailable.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HangingSyncBackplane : ISyncBackplane
    {
        public bool IsEnabled => true;

        public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncBackplaneHealth.Unavailable);
        }

        public async Task<IDisposable> SubscribeAsync(
            string room,
            Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new DisposableAction(static () => { });
        }

        public async Task<SyncBackplanePublishResult> PublishAsync(
            string room,
            SyncBackplaneMessage message,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new SyncBackplanePublishResult(Published: true, RemoteSubscribers: 1);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LateCompletingSubscribeBackplane : ISyncBackplane
    {
        private readonly TaskCompletionSource<IDisposable> subscribeCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsEnabled => true;

        public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(1)));
        }

        public Task<IDisposable> SubscribeAsync(
            string room,
            Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            return subscribeCompletion.Task;
        }

        public Task<SyncBackplanePublishResult> PublishAsync(
            string room,
            SyncBackplaneMessage message,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new SyncBackplanePublishResult(Published: true, RemoteSubscribers: 1));
        }

        public void CompleteSubscribe(IDisposable subscription)
        {
            subscribeCompletion.SetResult(subscription);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action dispose;

        public DisposableAction(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            dispose();
        }
    }
}
