using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public sealed class SyncAdmissionCoordinator<TConnection>
    where TConnection : notnull
{
    private readonly SyncRoomRegistry<TConnection> rooms;
    private readonly ISyncAdmissionController admissionController;
    private readonly int maxRooms;
    private readonly int maxPeersPerRoom;
    private readonly TimeSpan operationTimeout;
    private readonly SyncMetrics metrics;
    private readonly ILogger logger;
    private readonly int maxReconcileConcurrency;
    private readonly ConcurrentDictionary<Guid, string> distributedAdmissions = new();

    public SyncAdmissionCoordinator(
        SyncRoomRegistry<TConnection> rooms,
        ISyncAdmissionController admissionController,
        int maxRooms,
        int maxPeersPerRoom,
        TimeSpan operationTimeout,
        SyncMetrics metrics,
        ILogger logger,
        int maxReconcileConcurrency = 4)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(admissionController);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRooms);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPeersPerRoom);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(operationTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxReconcileConcurrency);

        this.rooms = rooms;
        this.admissionController = admissionController;
        this.maxRooms = maxRooms;
        this.maxPeersPerRoom = maxPeersPerRoom;
        this.operationTimeout = operationTimeout;
        this.metrics = metrics;
        this.logger = logger;
        this.maxReconcileConcurrency = maxReconcileConcurrency;
    }

    public bool IsDistributed => admissionController.IsDistributed;

    public async Task<SyncJoinResult> TryJoinAsync(
        string room,
        Guid connectionId,
        TConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(connection);

        var localResult = rooms.TryJoin(room, connectionId, connection);
        if (localResult is not SyncJoinResult.Joined)
        {
            return localResult;
        }

        if (!admissionController.IsDistributed)
        {
            return SyncJoinResult.Joined;
        }

        try
        {
            using var timeout = CreateOperationTimeout(cancellationToken);
            var distributedResult = await admissionController.TryJoinAsync(
                room,
                connectionId,
                maxRooms,
                maxPeersPerRoom,
                timeout.Token).WaitAsync(timeout.Token);
            if (distributedResult is SyncJoinResult.Joined)
            {
                distributedAdmissions[connectionId] = room;
                return SyncJoinResult.Joined;
            }

            rooms.Leave(room, connectionId);
            metrics.AdmissionRejected(distributedResult);
            return distributedResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            rooms.Leave(room, connectionId);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.AdmissionControllerFailed();
            SyncLog.AdmissionControllerJoinFailed(logger, exception, room);
            return SyncJoinResult.Joined;
        }
    }

    public async Task ReconcileActiveAdmissionsAsync(CancellationToken cancellationToken)
    {
        if (!admissionController.IsDistributed)
        {
            return;
        }

        var candidates = rooms.GetRooms()
            .SelectMany(room => rooms.GetPeers(room)
                .Select(peer => (Room: room, ConnectionId: peer.Key)))
            .Where(candidate => !distributedAdmissions.ContainsKey(candidate.ConnectionId))
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = maxReconcileConcurrency
            },
            async (candidate, token) => await ReconcileAdmissionAsync(
                candidate.Room,
                candidate.ConnectionId,
                token));
    }

    public async Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        rooms.Leave(room, connectionId);

        if (!admissionController.IsDistributed ||
            !distributedAdmissions.TryRemove(connectionId, out var admittedRoom))
        {
            return;
        }

        try
        {
            using var timeout = CreateOperationTimeout(cancellationToken);
            await admissionController.PeerLeftAsync(admittedRoom, connectionId, timeout.Token)
                .WaitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.AdmissionControllerFailed();
            SyncLog.AdmissionControllerLeaveFailed(logger, exception, admittedRoom);
        }
    }

    private async ValueTask ReconcileAdmissionAsync(
        string room,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (distributedAdmissions.ContainsKey(connectionId) ||
            !rooms.Contains(room, connectionId))
        {
            return;
        }

        SyncJoinResult distributedResult;
        try
        {
            using var timeout = CreateOperationTimeout(cancellationToken);
            distributedResult = await admissionController.TryJoinAsync(
                room,
                connectionId,
                maxRooms,
                maxPeersPerRoom,
                timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.AdmissionControllerFailed();
            SyncLog.AdmissionControllerJoinFailed(logger, exception, room);
            return;
        }

        if (distributedResult is not SyncJoinResult.Joined)
        {
            metrics.AdmissionRejected(distributedResult);
            return;
        }

        if (!distributedAdmissions.TryAdd(connectionId, room))
        {
            return;
        }

        if (!rooms.Contains(room, connectionId) &&
            distributedAdmissions.TryRemove(connectionId, out var admittedRoom))
        {
            await ReleaseDistributedAdmissionAsync(admittedRoom, connectionId, cancellationToken);
        }
    }

    private async Task ReleaseDistributedAdmissionAsync(
        string admittedRoom,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CreateOperationTimeout(cancellationToken);
            await admissionController.PeerLeftAsync(admittedRoom, connectionId, timeout.Token)
                .WaitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.AdmissionControllerFailed();
            SyncLog.AdmissionControllerLeaveFailed(logger, exception, admittedRoom);
        }
    }

    private CancellationTokenSource CreateOperationTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operationTimeout);
        return timeout;
    }
}
