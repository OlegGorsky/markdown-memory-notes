using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public sealed class SyncBackplaneBridge<TConnection> : IDisposable
    where TConnection : notnull
{
    private readonly string instanceId;
    private readonly SyncRoomRegistry<TConnection> rooms;
    private readonly SyncBroadcaster<TConnection> broadcaster;
    private readonly ISyncBackplane backplane;
    private readonly TimeSpan sendTimeout;
    private readonly ILogger logger;
    private readonly Dictionary<string, IDisposable> subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim subscriptionGate = new(1, 1);

    public SyncBackplaneBridge(
        string instanceId,
        SyncRoomRegistry<TConnection> rooms,
        SyncBroadcaster<TConnection> broadcaster,
        ISyncBackplane backplane,
        TimeSpan sendTimeout,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(backplane);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sendTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        this.instanceId = instanceId;
        this.rooms = rooms;
        this.broadcaster = broadcaster;
        this.backplane = backplane;
        this.sendTimeout = sendTimeout;
        this.logger = logger;
    }

    public async Task<SyncBackplanePublishResult> PublishAsync(
        string room,
        Guid senderConnectionId,
        string payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(payload);

        if (!backplane.IsEnabled)
        {
            return new SyncBackplanePublishResult(Published: false, RemoteSubscribers: 0);
        }

        try
        {
            return await backplane.PublishAsync(
                room,
                new SyncBackplaneMessage(instanceId, senderConnectionId, payload),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            SyncLog.BackplanePublishFailed(logger, exception, room);
            return new SyncBackplanePublishResult(Published: false, RemoteSubscribers: 0);
        }
    }

    public async Task EnsureSubscribedAsync(string room, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        if (!backplane.IsEnabled)
        {
            return;
        }

        await subscriptionGate.WaitAsync(cancellationToken);
        try
        {
            if (subscriptions.ContainsKey(room))
            {
                return;
            }

            try
            {
                subscriptions[room] = await backplane.SubscribeAsync(
                    room,
                    (message, token) => ReceiveAsync(room, message, token),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                                              !cancellationToken.IsCancellationRequested)
            {
                SyncLog.BackplaneSubscribeFailed(logger, exception, room);
            }
        }
        finally
        {
            subscriptionGate.Release();
        }
    }

    public async Task ReleaseIfRoomEmptyAsync(string room)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        if (!backplane.IsEnabled)
        {
            return;
        }

        await subscriptionGate.WaitAsync();
        try
        {
            if (rooms.GetPeers(room).Count > 0)
            {
                return;
            }

            if (subscriptions.Remove(room, out var subscription))
            {
                subscription.Dispose();
            }
        }
        finally
        {
            subscriptionGate.Release();
        }
    }

    public async Task<SyncBroadcastResult> ReceiveAsync(
        string room,
        SyncBackplaneMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(message);

        if (string.Equals(message.OriginInstanceId, instanceId, StringComparison.Ordinal))
        {
            return new SyncBroadcastResult(Attempted: 0, Succeeded: 0, Failed: 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await broadcaster.BroadcastAsync(
            room,
            senderId: Guid.Empty,
            message.Payload,
            sendTimeout,
            logger);
    }

    public void Dispose()
    {
        subscriptionGate.Wait();
        try
        {
            foreach (var subscription in subscriptions.Values)
            {
                subscription.Dispose();
            }

            subscriptions.Clear();
        }
        finally
        {
            subscriptionGate.Release();
            subscriptionGate.Dispose();
        }
    }
}
