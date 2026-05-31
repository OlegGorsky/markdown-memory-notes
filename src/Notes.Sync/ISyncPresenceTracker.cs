namespace Notes.Sync;

public interface ISyncPresenceTracker
{
    bool IsDistributed { get; }

    Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken);

    Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken);

    Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken);
}

public sealed class NoopSyncPresenceTracker : ISyncPresenceTracker
{
    public static NoopSyncPresenceTracker Instance { get; } = new();

    public bool IsDistributed => false;

    public Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        return Task.CompletedTask;
    }

    public Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        return Task.CompletedTask;
    }

    public Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        return Task.FromResult<int?>(null);
    }
}
