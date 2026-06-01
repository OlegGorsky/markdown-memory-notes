using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public sealed class RecoveringSyncBackplane :
    ISyncBackplane,
    ISyncPresenceTracker,
    ISyncAdmissionController
{
    private readonly string connectionString;
    private readonly string channelPrefix;
    private readonly string instanceId;
    private readonly int maxReceiveQueue;
    private readonly SyncMetrics metrics;
    private readonly ILogger logger;
    private readonly SyncRedisBackplaneConnector connectRedisAsync;
    private readonly TimeSpan reconnectDelay;
    private readonly Func<DateTimeOffset> now;
    private readonly SemaphoreSlim reconnectGate = new(1, 1);
    private readonly Lock stateGate = new();
    private ISyncBackplane? current;
    private ISyncPresenceTracker? currentPresenceTracker;
    private ISyncAdmissionController? currentAdmissionController;
    private DateTimeOffset nextReconnectAt;
    private int disposed;

    public RecoveringSyncBackplane(
        string connectionString,
        string channelPrefix,
        string instanceId,
        int maxReceiveQueue,
        SyncMetrics metrics,
        ILogger logger,
        SyncRedisBackplaneConnector connectRedisAsync,
        TimeSpan reconnectDelay,
        Func<DateTimeOffset>? now = null,
        bool startInBackoff = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxReceiveQueue);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connectRedisAsync);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reconnectDelay, TimeSpan.Zero);

        this.connectionString = connectionString;
        this.channelPrefix = channelPrefix;
        this.instanceId = instanceId;
        this.maxReceiveQueue = maxReceiveQueue;
        this.metrics = metrics;
        this.logger = logger;
        this.connectRedisAsync = connectRedisAsync;
        this.reconnectDelay = reconnectDelay;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        nextReconnectAt = startInBackoff
            ? this.now() + reconnectDelay
            : DateTimeOffset.MinValue;
    }

    public bool IsEnabled => true;

    public bool IsDistributed
    {
        get
        {
            lock (stateGate)
            {
                return currentAdmissionController?.IsDistributed == true ||
                       currentPresenceTracker?.IsDistributed == true;
            }
        }
    }

    public async Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var backplane = await GetCurrentOrReconnectAsync(cancellationToken);
        return backplane is null
            ? SyncBackplaneHealth.Unavailable
            : await backplane.CheckHealthAsync(cancellationToken);
    }

    public async Task<IDisposable> SubscribeAsync(
        string room,
        Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(onMessage);
        var backplane = await GetCurrentOrReconnectAsync(cancellationToken);
        if (backplane is null)
        {
            throw new InvalidOperationException("Sync backplane is unavailable.");
        }

        return await backplane.SubscribeAsync(room, onMessage, cancellationToken);
    }

    public async Task<SyncBackplanePublishResult> PublishAsync(
        string room,
        SyncBackplaneMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(message);
        var backplane = await GetCurrentOrReconnectAsync(cancellationToken);
        return backplane is null
            ? new SyncBackplanePublishResult(Published: false, RemoteSubscribers: 0)
            : await backplane.PublishAsync(room, message, cancellationToken);
    }

    public async Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        var tracker = await GetPresenceTrackerOrReconnectAsync(cancellationToken);
        if (tracker is not null)
        {
            await tracker.PeerJoinedAsync(room, connectionId, cancellationToken);
        }
    }

    public async Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        var tracker = await GetPresenceTrackerOrReconnectAsync(cancellationToken);
        if (tracker is not null)
        {
            await tracker.PeerLeftAsync(room, connectionId, cancellationToken);
        }
    }

    public async Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        var tracker = await GetPresenceTrackerOrReconnectAsync(cancellationToken);
        return tracker is null
            ? null
            : await tracker.GetPeerCountAsync(room, cancellationToken);
    }

    public async Task<SyncJoinResult> TryJoinAsync(
        string room,
        Guid connectionId,
        int maxRooms,
        int maxPeersPerRoom,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRooms);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPeersPerRoom);
        var admission = await GetAdmissionControllerOrReconnectAsync(cancellationToken);
        return admission is null
            ? SyncJoinResult.Joined
            : await admission.TryJoinAsync(room, connectionId, maxRooms, maxPeersPerRoom, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        ISyncBackplane? backplane;
        lock (stateGate)
        {
            backplane = current;
            current = null;
            currentPresenceTracker = null;
            currentAdmissionController = null;
        }

        if (backplane is not null)
        {
            await backplane.DisposeAsync();
        }

        reconnectGate.Dispose();
    }

    private async Task<ISyncPresenceTracker?> GetPresenceTrackerOrReconnectAsync(CancellationToken cancellationToken)
    {
        await GetCurrentOrReconnectAsync(cancellationToken);
        lock (stateGate)
        {
            return currentPresenceTracker;
        }
    }

    private async Task<ISyncAdmissionController?> GetAdmissionControllerOrReconnectAsync(CancellationToken cancellationToken)
    {
        await GetCurrentOrReconnectAsync(cancellationToken);
        lock (stateGate)
        {
            return currentAdmissionController;
        }
    }

    private async Task<ISyncBackplane?> GetCurrentOrReconnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        lock (stateGate)
        {
            if (current is not null)
            {
                return current;
            }

            if (now() < nextReconnectAt)
            {
                return null;
            }
        }

        await reconnectGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            lock (stateGate)
            {
                if (current is not null)
                {
                    return current;
                }

                if (now() < nextReconnectAt)
                {
                    return null;
                }
            }

            try
            {
                var connected = await connectRedisAsync(
                    connectionString,
                    channelPrefix,
                    instanceId,
                    maxReceiveQueue,
                    metrics,
                    logger);
                lock (stateGate)
                {
                    current = connected;
                    currentPresenceTracker = connected as ISyncPresenceTracker;
                    currentAdmissionController = connected as ISyncAdmissionController;
                }

                return connected;
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                RecordConnectionFailure(exception);
                return null;
            }
        }
        finally
        {
            reconnectGate.Release();
        }
    }

    private void RecordConnectionFailure(Exception exception)
    {
        metrics.BackplaneHealthCheckFailed();
        SyncLog.BackplaneConnectFailed(logger, exception);
        lock (stateGate)
        {
            nextReconnectAt = now() + reconnectDelay;
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is System.TimeoutException ||
               exception.GetType().FullName is
                   "StackExchange.Redis.RedisConnectionException" or
                   "StackExchange.Redis.RedisTimeoutException";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
    }
}
