using Microsoft.Extensions.Logging.Abstractions;
using Notes.Core.Sync;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncPresenceBroadcasterTests
{
    private const string Room = "RoomPresence-ABCDEFGHI";

    [Fact]
    public async Task BroadcastAsyncSendsPeerCountToEveryPeer()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var firstPeer = new TestPeer();
        var secondPeer = new TestPeer();
        registry.TryJoin(Room, Guid.NewGuid(), firstPeer);
        registry.TryJoin(Room, Guid.NewGuid(), secondPeer);

        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            SendAsync,
            maxFanoutConcurrency: 4,
            new SyncMetrics());

        await SyncPresenceBroadcaster.BroadcastAsync(
            Room,
            registry,
            broadcaster,
            TimeSpan.FromSeconds(1),
            NullLogger.Instance);

        var firstMessage = Assert.Single(firstPeer.Messages);
        var secondMessage = Assert.Single(secondPeer.Messages);
        Assert.True(SyncPresenceMessage.TryParse(firstMessage, out var firstPeerCount));
        Assert.True(SyncPresenceMessage.TryParse(secondMessage, out var secondPeerCount));
        Assert.Equal(2, firstPeerCount);
        Assert.Equal(2, secondPeerCount);
    }

    [Fact]
    public async Task BroadcastAsyncSkipsEmptyRooms()
    {
        var registry = new SyncRoomRegistry<TestPeer>(maxRooms: 1, maxPeersPerRoom: 4);
        var sendCount = 0;
        var broadcaster = new SyncBroadcaster<TestPeer>(
            registry,
            static peer => peer.IsOpen,
            (_, _, _) =>
            {
                sendCount++;
                return Task.CompletedTask;
            },
            maxFanoutConcurrency: 4,
            new SyncMetrics());

        await SyncPresenceBroadcaster.BroadcastAsync(
            Room,
            registry,
            broadcaster,
            TimeSpan.FromSeconds(1),
            NullLogger.Instance);

        Assert.Equal(0, sendCount);
    }

    private static Task SendAsync(TestPeer peer, string payload, CancellationToken cancellationToken)
    {
        peer.Messages.Add(payload);
        return Task.CompletedTask;
    }

    private sealed class TestPeer
    {
        public bool IsOpen { get; init; } = true;
        public List<string> Messages { get; } = new();
    }
}
