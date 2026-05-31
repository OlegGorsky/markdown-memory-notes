using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public sealed record SyncBroadcastResult(int Attempted, int Succeeded, int Failed);

public sealed class SyncBroadcaster<TConnection>
    where TConnection : notnull
{
    private readonly SyncRoomRegistry<TConnection> rooms;
    private readonly Func<TConnection, bool> isOpen;
    private readonly Func<TConnection, string, CancellationToken, Task> sendAsync;
    private readonly int maxFanoutConcurrency;
    private readonly SyncMetrics metrics;

    public SyncBroadcaster(
        SyncRoomRegistry<TConnection> rooms,
        Func<TConnection, bool> isOpen,
        Func<TConnection, string, CancellationToken, Task> sendAsync,
        int maxFanoutConcurrency,
        SyncMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFanoutConcurrency);

        this.rooms = rooms;
        this.isOpen = isOpen;
        this.sendAsync = sendAsync;
        this.maxFanoutConcurrency = maxFanoutConcurrency;
        this.metrics = metrics;
    }

    public async Task<SyncBroadcastResult> BroadcastAsync(
        string room,
        Guid senderId,
        string message,
        TimeSpan sendTimeout,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sendTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        var peers = rooms.GetPeers(room)
            .Where(peer => peer.Key != senderId)
            .ToArray();
        metrics.DeliveryAttempted(peers.Length);
        var succeeded = 0;
        var failed = 0;

        await Parallel.ForEachAsync(
            peers,
            new ParallelOptions { MaxDegreeOfParallelism = maxFanoutConcurrency },
            async (peer, _) =>
            {
                if (!isOpen(peer.Value))
                {
                    rooms.Leave(room, peer.Key);
                    metrics.PeerRemoved();
                    Interlocked.Increment(ref failed);
                    return;
                }

                using var timeout = new CancellationTokenSource(sendTimeout);
                try
                {
                    await sendAsync(peer.Value, message, timeout.Token);
                    metrics.DeliverySucceeded();
                    Interlocked.Increment(ref succeeded);
                }
                catch (Exception exception) when (IsUnavailablePeerException(exception))
                {
                    SyncLog.RemovingUnavailablePeer(logger, exception, room);
                    rooms.Leave(room, peer.Key);
                    metrics.DeliveryFailed();
                    metrics.PeerRemoved();
                    Interlocked.Increment(ref failed);
                }
            });

        return new SyncBroadcastResult(peers.Length, succeeded, failed);
    }

    private static bool IsUnavailablePeerException(Exception exception)
    {
        return exception is WebSocketException or
            OperationCanceledException or
            InvalidOperationException or
            ObjectDisposedException;
    }
}
