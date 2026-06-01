using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class RecoveringSyncBackplaneTests
{
    [Fact]
    public async Task CheckHealthAsyncReconnectsAfterRetryDelay()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var attempts = 0;
        await using var recovered = new RecordingDistributedBackplane();
        await using var backplane = new RecoveringSyncBackplane(
            connectionString: "redis.internal:6379",
            channelPrefix: "mmn:sync:test",
            instanceId: "relay-a",
            maxReceiveQueue: 128,
            metrics: new SyncMetrics(),
            logger: NullLogger.Instance,
            connectRedisAsync: ConnectAsync,
            reconnectDelay: TimeSpan.FromSeconds(1),
            now: () => now);

        var first = await backplane.CheckHealthAsync(CancellationToken.None);
        now = now.AddMilliseconds(500);
        var duringBackoff = await backplane.CheckHealthAsync(CancellationToken.None);
        now = now.AddMilliseconds(600);
        var afterBackoff = await backplane.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.False(first.Healthy);
        Assert.False(duringBackoff.Healthy);
        Assert.True(afterBackoff.Healthy);
        Assert.True(backplane.IsDistributed);

        Task<ISyncBackplane> ConnectAsync(
            string connectionString,
            string channelPrefix,
            string instanceId,
            int maxReceiveQueue,
            SyncMetrics metrics,
            ILogger logger)
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("Redis unavailable.");
            }

            return Task.FromResult<ISyncBackplane>(recovered);
        }
    }

    [Fact]
    public async Task PublishAsyncReconnectsAndDelegatesToRecoveredBackplane()
    {
        var attempts = 0;
        await using var recovered = new RecordingDistributedBackplane();
        await using var backplane = new RecoveringSyncBackplane(
            connectionString: "redis.internal:6379",
            channelPrefix: "mmn:sync:test",
            instanceId: "relay-a",
            maxReceiveQueue: 128,
            metrics: new SyncMetrics(),
            logger: NullLogger.Instance,
            connectRedisAsync: (_, _, _, _, _, _) =>
            {
                attempts++;
                return Task.FromResult<ISyncBackplane>(recovered);
            },
            reconnectDelay: TimeSpan.FromSeconds(1));

        var result = await backplane.PublishAsync(
            "RoomBackplaneRecover-ABC",
            new SyncBackplaneMessage("relay-a", Guid.NewGuid(), """{"type":"heartbeat"}"""),
            CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.True(result.Published);
        Assert.Equal(1, result.RemoteSubscribers);
        Assert.Equal(1, recovered.PublishCount);
    }

    private sealed class RecordingDistributedBackplane :
        ISyncBackplane,
        ISyncPresenceTracker,
        ISyncAdmissionController
    {
        public bool IsEnabled => true;
        public bool IsDistributed => true;
        public int PublishCount { get; private set; }

        public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(1)));
        }

        public Task<IDisposable> SubscribeAsync(
            string room,
            Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IDisposable>(new EmptySubscription());
        }

        public Task<SyncBackplanePublishResult> PublishAsync(
            string room,
            SyncBackplaneMessage message,
            CancellationToken cancellationToken)
        {
            PublishCount++;
            return Task.FromResult(new SyncBackplanePublishResult(Published: true, RemoteSubscribers: 1));
        }

        public Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
        {
            return Task.FromResult<int?>(1);
        }

        public Task<SyncJoinResult> TryJoinAsync(
            string room,
            Guid connectionId,
            int maxRooms,
            int maxPeersPerRoom,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncJoinResult.Joined);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private sealed class EmptySubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
