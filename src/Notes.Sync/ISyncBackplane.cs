namespace Notes.Sync;

public interface ISyncBackplane : IAsyncDisposable
{
    bool IsEnabled { get; }

    Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken);

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

public sealed record SyncBackplaneHealth(
    bool Enabled,
    bool Healthy,
    long? LatencyMilliseconds,
    string Status)
{
    public static SyncBackplaneHealth Disabled { get; } = new(
        Enabled: false,
        Healthy: true,
        LatencyMilliseconds: null,
        Status: "disabled");

    public static SyncBackplaneHealth Available(TimeSpan latency)
    {
        return new SyncBackplaneHealth(
            Enabled: true,
            Healthy: true,
            LatencyMilliseconds: Math.Max(0L, (long)Math.Ceiling(latency.TotalMilliseconds)),
            Status: "ok");
    }

    public static SyncBackplaneHealth Unavailable { get; } = new(
        Enabled: true,
        Healthy: false,
        LatencyMilliseconds: null,
        Status: "unavailable");
}

public sealed class NoopSyncBackplane : ISyncBackplane
{
    private static readonly IDisposable EmptySubscription = new EmptyDisposable();

    public static NoopSyncBackplane Instance { get; } = new();

    public bool IsEnabled => false;

    public Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SyncBackplaneHealth.Disabled);
    }

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
