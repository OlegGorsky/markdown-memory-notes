using Microsoft.Extensions.Logging;
using Notes.Core.Sync;

namespace Notes.Sync;

public sealed class SyncPresenceCoordinator<TConnection> : IDisposable
    where TConnection : notnull
{
    private static readonly Guid ServerSenderId = Guid.Empty;

    private readonly SyncRoomRegistry<TConnection> rooms;
    private readonly SyncBroadcaster<TConnection> broadcaster;
    private readonly SyncBackplaneBridge<TConnection> backplaneBridge;
    private readonly ISyncPresenceTracker presenceTracker;
    private readonly TimeSpan sendTimeout;
    private readonly SyncMetrics metrics;
    private readonly ILogger logger;

    public SyncPresenceCoordinator(
        SyncRoomRegistry<TConnection> rooms,
        SyncBroadcaster<TConnection> broadcaster,
        SyncBackplaneBridge<TConnection> backplaneBridge,
        ISyncPresenceTracker presenceTracker,
        TimeSpan sendTimeout,
        SyncMetrics metrics,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(backplaneBridge);
        ArgumentNullException.ThrowIfNull(presenceTracker);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sendTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        this.rooms = rooms;
        this.broadcaster = broadcaster;
        this.backplaneBridge = backplaneBridge;
        this.presenceTracker = presenceTracker;
        this.sendTimeout = sendTimeout;
        this.metrics = metrics;
        this.logger = logger;
    }

    public bool IsDistributed => presenceTracker.IsDistributed;

    public Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        return UpdatePresenceAsync(room, connectionId, joined: true, cancellationToken);
    }

    public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        return UpdatePresenceAsync(room, connectionId, joined: false, cancellationToken);
    }

    public Task BroadcastAsync(string room, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        return BroadcastPresenceAsync(room, cancellationToken);
    }

    public void Dispose()
    {
        backplaneBridge.Dispose();
    }

    private async Task UpdatePresenceAsync(
        string room,
        Guid connectionId,
        bool joined,
        CancellationToken cancellationToken)
    {
        if (joined)
        {
            await TrackJoinSafeAsync(room, connectionId, cancellationToken);
        }
        else
        {
            await TrackLeaveSafeAsync(room, connectionId, cancellationToken);
        }

        await BroadcastPresenceAsync(room, cancellationToken);
    }

    private async Task BroadcastPresenceAsync(string room, CancellationToken cancellationToken)
    {
        var localPeerCount = rooms.GetPeers(room).Count;
        var peerCount = await GetPeerCountSafeAsync(room, localPeerCount, cancellationToken);
        if (peerCount <= 0)
        {
            return;
        }

        var payload = SyncPresenceMessage.Create(peerCount);
        if (localPeerCount > 0)
        {
            await broadcaster.BroadcastAsync(room, ServerSenderId, payload, sendTimeout, logger);
        }

        await backplaneBridge.PublishAsync(room, ServerSenderId, payload, cancellationToken);
    }

    private async Task TrackJoinSafeAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            await presenceTracker.PeerJoinedAsync(room, connectionId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.PresenceTrackerJoinFailed();
            SyncLog.PresenceTrackerJoinFailed(logger, exception, room);
        }
    }

    private async Task TrackLeaveSafeAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            await presenceTracker.PeerLeftAsync(room, connectionId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.PresenceTrackerLeaveFailed();
            SyncLog.PresenceTrackerLeaveFailed(logger, exception, room);
        }
    }

    private async Task<int> GetPeerCountSafeAsync(
        string room,
        int localPeerCount,
        CancellationToken cancellationToken)
    {
        try
        {
            return await presenceTracker.GetPeerCountAsync(room, cancellationToken) ?? localPeerCount;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.PresenceTrackerCountFailed();
            SyncLog.PresenceTrackerCountFailed(logger, exception, room);
            return localPeerCount;
        }
    }
}
