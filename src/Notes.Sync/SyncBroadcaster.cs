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
    private readonly Lock sendGateLock = new();
    private readonly Dictionary<Guid, SendGate> sendGates = new();
    private Func<string, Guid, CancellationToken, Task>? peerRemovedHandler;

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

    public int SendGateCount
    {
        get
        {
            lock (sendGateLock)
            {
                return sendGates.Count;
            }
        }
    }

    public void SetPeerRemovedHandler(Func<string, Guid, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        peerRemovedHandler = handler;
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
                    await RemovePeerAndRecordAsync(room, peer.Key, logger, CancellationToken.None);
                    metrics.DeliveryFailed();
                    Interlocked.Increment(ref failed);
                    return;
                }

                using var timeout = new CancellationTokenSource(sendTimeout);
                var sendGate = GetReferencedSendGate(peer.Key);
                try
                {
                    using (await sendGate.AcquireAsync(timeout.Token))
                    {
                        await sendAsync(peer.Value, message, timeout.Token);
                    }

                    metrics.DeliverySucceeded();
                    Interlocked.Increment(ref succeeded);
                }
                catch (Exception exception) when (IsUnavailablePeerException(exception))
                {
                    SyncLog.RemovingUnavailablePeer(logger, exception, room);
                    await RemovePeerAndRecordAsync(room, peer.Key, logger, CancellationToken.None);
                    metrics.DeliveryFailed();
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    sendGate.ReleaseReference();
                    TryRemoveForgottenGate(peer.Key, sendGate);
                }
            });

        return new SyncBroadcastResult(peers.Length, succeeded, failed);
    }

    public async Task<bool> SendToPeerAsync(
        string room,
        Guid connectionId,
        TConnection connection,
        string message,
        TimeSpan sendTimeout,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sendTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        if (!isOpen(connection))
        {
            await RemovePeerAndRecordAsync(room, connectionId, logger, CancellationToken.None);
            return false;
        }

        using var timeout = new CancellationTokenSource(sendTimeout);
        var sendGate = GetReferencedSendGate(connectionId);
        try
        {
            using (await sendGate.AcquireAsync(timeout.Token))
            {
                await sendAsync(connection, message, timeout.Token);
            }

            return true;
        }
        catch (Exception exception) when (IsUnavailablePeerException(exception))
        {
            SyncLog.RemovingUnavailablePeer(logger, exception, room);
            await RemovePeerAndRecordAsync(room, connectionId, logger, CancellationToken.None);
            return false;
        }
        finally
        {
            sendGate.ReleaseReference();
            TryRemoveForgottenGate(connectionId, sendGate);
        }
    }

    public void ForgetPeer(Guid connectionId)
    {
        lock (sendGateLock)
        {
            if (sendGates.TryGetValue(connectionId, out var sendGate))
            {
                sendGate.Forget();
                TryRemoveForgottenGateNoLock(connectionId, sendGate);
            }
        }
    }

    private static bool IsUnavailablePeerException(Exception exception)
    {
        return exception is WebSocketException or
            OperationCanceledException or
            InvalidOperationException or
            ObjectDisposedException;
    }

    private async Task<bool> RemovePeerAndRecordAsync(
        string room,
        Guid connectionId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!await RemovePeerAsync(room, connectionId, logger, cancellationToken))
        {
            return false;
        }

        metrics.PeerRemoved();
        return true;
    }

    private async Task<bool> RemovePeerAsync(
        string room,
        Guid connectionId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!rooms.Leave(room, connectionId))
        {
            return false;
        }

        ForgetPeer(connectionId);
        if (peerRemovedHandler is not null)
        {
            try
            {
                await peerRemovedHandler(room, connectionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                                              !cancellationToken.IsCancellationRequested)
            {
                metrics.PeerCleanupFailed();
                SyncLog.PeerCleanupFailed(logger, exception, room);
            }
        }

        return true;
    }

    private void TryRemoveForgottenGate(Guid connectionId, SendGate sendGate)
    {
        lock (sendGateLock)
        {
            TryRemoveForgottenGateNoLock(connectionId, sendGate);
        }
    }

    private void TryRemoveForgottenGateNoLock(Guid connectionId, SendGate sendGate)
    {
        if (sendGate.CanRemove &&
            sendGates.TryGetValue(connectionId, out var current) &&
            ReferenceEquals(current, sendGate))
        {
            sendGates.Remove(connectionId);
            sendGate.Dispose();
        }
    }

    private SendGate GetReferencedSendGate(Guid connectionId)
    {
        lock (sendGateLock)
        {
            if (!sendGates.TryGetValue(connectionId, out var sendGate))
            {
                sendGate = new SendGate();
                sendGates[connectionId] = sendGate;
            }

            sendGate.AddReference();
            return sendGate;
        }
    }

    private sealed class SendGate : IDisposable
    {
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private int references;
        private int forgotten;

        public bool CanRemove =>
            Volatile.Read(ref forgotten) == 1 &&
            Volatile.Read(ref references) == 0;

        public void AddReference()
        {
            Interlocked.Increment(ref references);
        }

        public async Task<Lease> AcquireAsync(CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);
            return new Lease(this);
        }

        public void Forget()
        {
            Volatile.Write(ref forgotten, 1);
        }

        private void Release()
        {
            semaphore.Release();
        }

        public void ReleaseReference()
        {
            Interlocked.Decrement(ref references);
        }

        public void Dispose()
        {
            semaphore.Dispose();
        }

        public readonly struct Lease : IDisposable
        {
            private readonly SendGate? gate;

            public Lease(SendGate gate)
            {
                this.gate = gate;
            }

            public void Dispose()
            {
                gate?.Release();
            }
        }
    }
}
