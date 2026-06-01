using System.Text;
using Microsoft.Extensions.Logging;
using Notes.Core.Sync;

namespace Notes.Sync;

public sealed class SyncBackplaneBridge<TConnection> : IDisposable
    where TConnection : notnull
{
    private readonly string instanceId;
    private readonly SyncRoomRegistry<TConnection> rooms;
    private readonly SyncBroadcaster<TConnection> broadcaster;
    private readonly ISyncBackplane backplane;
    private readonly int maxMessageBytes;
    private readonly TimeSpan sendTimeout;
    private readonly SyncMetrics metrics;
    private readonly ILogger logger;
    private readonly Lock receiveGateLock = new();
    private readonly Dictionary<string, ReceiveGate> receiveGates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDisposable> subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim subscriptionGate = new(1, 1);
    private int subscriptionCount;

    public SyncBackplaneBridge(
        string instanceId,
        SyncRoomRegistry<TConnection> rooms,
        SyncBroadcaster<TConnection> broadcaster,
        ISyncBackplane backplane,
        int maxMessageBytes,
        TimeSpan sendTimeout,
        SyncMetrics metrics,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(backplane);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sendTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        this.instanceId = instanceId;
        this.rooms = rooms;
        this.broadcaster = broadcaster;
        this.backplane = backplane;
        this.maxMessageBytes = maxMessageBytes;
        this.sendTimeout = sendTimeout;
        this.metrics = metrics;
        this.logger = logger;
    }

    public int SubscriptionCount => Volatile.Read(ref subscriptionCount);

    public int ReceiveGateCount
    {
        get
        {
            lock (receiveGateLock)
            {
                return receiveGates.Count;
            }
        }
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

        metrics.BackplanePublishAttempted();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(sendTimeout);
            var result = await backplane.PublishAsync(
                room,
                new SyncBackplaneMessage(instanceId, senderConnectionId, payload),
                timeout.Token).WaitAsync(timeout.Token);
            if (result.Published)
            {
                metrics.BackplanePublishSucceeded(result.RemoteSubscribers);
            }
            else
            {
                metrics.BackplanePublishFailed();
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.BackplanePublishFailed();
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

            metrics.BackplaneSubscribeAttempted();
            Task<IDisposable>? subscribeTask = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(sendTimeout);
                subscribeTask = backplane.SubscribeAsync(
                    room,
                    (message, token) => ReceiveAsync(room, message, token),
                    timeout.Token);
                var subscription = await subscribeTask.WaitAsync(timeout.Token);
                subscriptions[room] = subscription;
                Interlocked.Increment(ref subscriptionCount);
                metrics.BackplaneSubscribeSucceeded();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DisposeSubscriptionWhenCompleted(subscribeTask, room, logger);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                                              !cancellationToken.IsCancellationRequested)
            {
                DisposeSubscriptionWhenCompleted(subscribeTask, room, logger);
                metrics.BackplaneSubscribeFailed();
                SyncLog.BackplaneSubscribeFailed(logger, exception, room);
            }
        }
        finally
        {
            subscriptionGate.Release();
        }
    }

    public async Task EnsureActiveSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (!backplane.IsEnabled)
        {
            return;
        }

        foreach (var room in rooms.GetRooms())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rooms.GetPeers(room).Count > 0)
            {
                await EnsureSubscribedAsync(room, cancellationToken);
            }
        }
    }

    private static void DisposeSubscriptionWhenCompleted(
        Task<IDisposable>? subscriptionTask,
        string room,
        ILogger logger)
    {
        if (subscriptionTask is null)
        {
            return;
        }

        _ = subscriptionTask.ContinueWith(
            static (completedTask, state) =>
            {
                var (room, logger) = ((string Room, ILogger Logger))state!;
                if (completedTask.IsCompletedSuccessfully)
                {
                    try
                    {
                        completedTask.Result.Dispose();
                    }
                    catch (OperationCanceledException exception)
                    {
                        SyncLog.BackplaneSubscribeFailed(logger, exception, room);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        SyncLog.BackplaneSubscribeFailed(logger, exception, room);
                    }
                }
                else if (completedTask.IsFaulted)
                {
                    _ = completedTask.Exception;
                }
            },
            (room, logger),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
                Interlocked.Decrement(ref subscriptionCount);
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
            metrics.BackplaneMessageIgnored();
            return new SyncBroadcastResult(Attempted: 0, Succeeded: 0, Failed: 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidRemotePayload(message.Payload))
        {
            metrics.BackplaneInvalidPayload();
            return new SyncBroadcastResult(Attempted: 0, Succeeded: 0, Failed: 0);
        }

        metrics.BackplaneMessageReceived();
        var receiveGate = GetReferencedReceiveGate(room);
        try
        {
            using (await receiveGate.AcquireAsync(cancellationToken))
            {
                return await broadcaster.BroadcastAsync(
                    room,
                    senderId: Guid.Empty,
                    message.Payload,
                    sendTimeout,
                    logger);
            }
        }
        finally
        {
            ReleaseReceiveGateReference(room, receiveGate);
        }
    }

    private bool IsValidRemotePayload(string payload)
    {
        return !string.IsNullOrWhiteSpace(payload) &&
               Encoding.UTF8.GetByteCount(payload) <= maxMessageBytes &&
               (SyncPresenceMessage.TryParse(payload, out _) ||
                SyncRelayMessage.IsValid(payload, maxMessageBytes));
    }

    private ReceiveGate GetReferencedReceiveGate(string room)
    {
        lock (receiveGateLock)
        {
            if (!receiveGates.TryGetValue(room, out var receiveGate))
            {
                receiveGate = new ReceiveGate();
                receiveGates[room] = receiveGate;
            }

            receiveGate.AddReference();
            return receiveGate;
        }
    }

    private void ReleaseReceiveGateReference(string room, ReceiveGate receiveGate)
    {
        lock (receiveGateLock)
        {
            receiveGate.ReleaseReference();
            if (receiveGate.CanRemove &&
                receiveGates.TryGetValue(room, out var current) &&
                ReferenceEquals(current, receiveGate))
            {
                receiveGates.Remove(room);
                receiveGate.Dispose();
            }
        }
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
            Volatile.Write(ref subscriptionCount, 0);
        }
        finally
        {
            subscriptionGate.Release();
            subscriptionGate.Dispose();
        }
    }

    private sealed class ReceiveGate : IDisposable
    {
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private int references;

        public bool CanRemove => Volatile.Read(ref references) == 0;

        public void AddReference()
        {
            Interlocked.Increment(ref references);
        }

        public async Task<Lease> AcquireAsync(CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);
            return new Lease(this);
        }

        public void ReleaseReference()
        {
            Interlocked.Decrement(ref references);
        }

        public void Dispose()
        {
            semaphore.Dispose();
        }

        private void Release()
        {
            semaphore.Release();
        }

        public readonly struct Lease : IDisposable
        {
            private readonly ReceiveGate? gate;

            public Lease(ReceiveGate gate)
            {
                this.gate = gate;
            }

            public void Dispose()
            {
                gate?.Release();
            }
        }
    }
}
