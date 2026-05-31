namespace Notes.Sync;

public interface ISyncAdmissionController
{
    bool IsDistributed { get; }

    Task<SyncJoinResult> TryJoinAsync(
        string room,
        Guid connectionId,
        int maxRooms,
        int maxPeersPerRoom,
        CancellationToken cancellationToken);

    Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken);
}

public sealed class NoopSyncAdmissionController : ISyncAdmissionController
{
    public static NoopSyncAdmissionController Instance { get; } = new();

    public bool IsDistributed => false;

    public Task<SyncJoinResult> TryJoinAsync(
        string room,
        Guid connectionId,
        int maxRooms,
        int maxPeersPerRoom,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SyncJoinResult.Joined);
    }

    public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
