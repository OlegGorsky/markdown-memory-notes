using Microsoft.Extensions.Logging.Abstractions;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBackplaneBridgeTests
{
    private const string Room = "RoomBackplane-ABCDEFGH";

    [Fact]
    public async Task ReceiveAsyncBroadcastsRemoteBackplaneMessagesToLocalPeers()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        using var bridge = CreateBridge("instance-a", registry);

        await bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-b", Guid.NewGuid(), "payload"),
            CancellationToken.None);

        Assert.Equal(["payload"], peer.Messages);
    }

    [Fact]
    public async Task ReceiveAsyncIgnoresMessagesFromSameInstance()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var peer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), peer);
        using var bridge = CreateBridge("instance-a", registry);

        await bridge.ReceiveAsync(
            Room,
            new SyncBackplaneMessage("instance-a", Guid.NewGuid(), "payload"),
            CancellationToken.None);

        Assert.Empty(peer.Messages);
    }

    [Fact]
    public async Task PublishAsyncPublishesLocalMessagesToBackplane()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new FakeSyncBackplane();
        using var bridge = CreateBridge("instance-a", registry, backplane);
        var senderId = Guid.NewGuid();

        var result = await bridge.PublishAsync(Room, senderId, "payload", CancellationToken.None);

        Assert.True(result.Published);
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
        using var bridge = CreateBridge("instance-a", registry, backplane);

        var result = await bridge.PublishAsync(Room, Guid.NewGuid(), "payload", CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(0, result.RemoteSubscribers);
    }

    [Fact]
    public async Task EnsureSubscribedAsyncDoesNotFailClientWhenBackplaneFails()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new ThrowingSyncBackplane();
        using var bridge = CreateBridge("instance-a", registry, backplane);

        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureSubscribedAsyncSubscribesOnlyOncePerRoom()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        await using var backplane = new FakeSyncBackplane();
        using var bridge = CreateBridge("instance-a", registry, backplane);

        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);
        await bridge.EnsureSubscribedAsync(Room, CancellationToken.None);

        Assert.Equal(1, backplane.SubscribeCount);
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
    }

    private static SyncBackplaneBridge<TestPeer> CreateBridge(
        string instanceId,
        SyncRoomRegistry<TestPeer> registry,
        ISyncBackplane? backplane = null)
    {
        var metrics = new SyncMetrics();
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

        return new SyncBackplaneBridge<TestPeer>(
            instanceId,
            registry,
            broadcaster,
            backplane ?? NoopSyncBackplane.Instance,
            TimeSpan.FromSeconds(1),
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
