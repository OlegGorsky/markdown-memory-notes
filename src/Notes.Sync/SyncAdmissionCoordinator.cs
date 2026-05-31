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
    private readonly SyncMetrics metrics;
    private readonly ILogger logger;
    private readonly ConcurrentDictionary<Guid, string> distributedAdmissions = new();

    public SyncAdmissionCoordinator(
        SyncRoomRegistry<TConnection> rooms,
        ISyncAdmissionController admissionController,
        int maxRooms,
        int maxPeersPerRoom,
        SyncMetrics metrics,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(admissionController);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRooms);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPeersPerRoom);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        this.rooms = rooms;
        this.admissionController = admissionController;
        this.maxRooms = maxRooms;
        this.maxPeersPerRoom = maxPeersPerRoom;
        this.metrics = metrics;
        this.logger = logger;
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
            var distributedResult = await admissionController.TryJoinAsync(
                room,
                connectionId,
                maxRooms,
                maxPeersPerRoom,
                cancellationToken);
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
            await admissionController.PeerLeftAsync(admittedRoom, connectionId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.AdmissionControllerFailed();
            SyncLog.AdmissionControllerLeaveFailed(logger, exception, admittedRoom);
        }
    }
}
