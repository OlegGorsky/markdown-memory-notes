namespace Notes.Sync;

public sealed class UnavailableSyncBackplane : ISyncBackplane
{
    public static UnavailableSyncBackplane Instance { get; } = new();

    public bool IsEnabled => true;

    public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SyncBackplaneHealth.Unavailable);
    }

    public Task<IDisposable> SubscribeAsync(
        string room,
        Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(onMessage);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Sync backplane is unavailable.");
    }

    public Task<SyncBackplanePublishResult> PublishAsync(
        string room,
        SyncBackplaneMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SyncBackplanePublishResult(Published: false, RemoteSubscribers: 0));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
