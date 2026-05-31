namespace Notes.Sync;

public interface ISyncBackplane : IAsyncDisposable
{
    bool IsEnabled { get; }

    Task<IDisposable> SubscribeAsync(
        string room,
        Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken);

    Task<SyncBackplanePublishResult> PublishAsync(
        string room,
        SyncBackplaneMessage message,
        CancellationToken cancellationToken);
}

public sealed record SyncBackplaneMessage(
    string OriginInstanceId,
    Guid SenderConnectionId,
    string Payload);

public sealed record SyncBackplanePublishResult(bool Published, int RemoteSubscribers);

public sealed class NoopSyncBackplane : ISyncBackplane
{
    private static readonly IDisposable EmptySubscription = new EmptyDisposable();

    public static NoopSyncBackplane Instance { get; } = new();

    public bool IsEnabled => false;

    public Task<IDisposable> SubscribeAsync(
        string room,
        Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(onMessage);
        return Task.FromResult(EmptySubscription);
    }

    public Task<SyncBackplanePublishResult> PublishAsync(
        string room,
        SyncBackplaneMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(message);
        return Task.FromResult(new SyncBackplanePublishResult(Published: false, RemoteSubscribers: 0));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
