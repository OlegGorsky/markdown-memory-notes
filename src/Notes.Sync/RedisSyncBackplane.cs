using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Notes.Sync;

public sealed class RedisSyncBackplane : ISyncBackplane, ISyncPresenceTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PresenceHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PresenceKeyTtl = TimeSpan.FromMinutes(5);
    private readonly ConnectionMultiplexer connection;
    private readonly ISubscriber subscriber;
    private readonly IDatabase database;
    private readonly string channelPrefix;
    private readonly string instanceId;
    private readonly ILogger logger;
    private readonly SyncMetrics metrics;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, RedisValue>> presenceMembers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource presenceHeartbeatStop = new();
    private readonly Task presenceHeartbeatTask;

    private RedisSyncBackplane(
        ConnectionMultiplexer connection,
        string channelPrefix,
        string instanceId,
        SyncMetrics metrics,
        ILogger logger)
    {
        this.connection = connection;
        this.subscriber = connection.GetSubscriber();
        this.database = connection.GetDatabase();
        this.channelPrefix = channelPrefix.Trim().TrimEnd(':');
        this.instanceId = instanceId;
        this.metrics = metrics;
        this.logger = logger;
        this.presenceHeartbeatTask = RunPresenceHeartbeatAsync();
    }

    public bool IsEnabled => true;
    public bool IsDistributed => true;

    public static async Task<RedisSyncBackplane> ConnectAsync(
        string connectionString,
        string channelPrefix,
        string instanceId,
        SyncMetrics metrics,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        return new RedisSyncBackplane(connection, channelPrefix, instanceId, metrics, logger);
    }

    public async Task<IDisposable> SubscribeAsync(
        string room,
        Func<SyncBackplaneMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(onMessage);
        cancellationToken.ThrowIfCancellationRequested();

        var channel = RedisChannel.Literal(ChannelName(room));
        await subscriber.SubscribeAsync(channel, async (_, value) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<SyncBackplaneMessage>(value.ToString(), JsonOptions);
                if (message is null)
                {
                    return;
                }

                await onMessage(message, CancellationToken.None);
            }
            catch (JsonException exception)
            {
                metrics.BackplaneInvalidPayload();
                SyncLog.BackplaneInvalidPayload(logger, exception, room);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                metrics.BackplaneReceiveFailed();
                SyncLog.BackplaneReceiveFailed(logger, exception, room);
            }
        });
        return new RedisSubscription(subscriber, channel);
    }

    public async Task<SyncBackplanePublishResult> PublishAsync(
        string room,
        SyncBackplaneMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var channel = RedisChannel.Literal(ChannelName(room));
        var payload = JsonSerializer.Serialize(message, JsonOptions);
        var subscribers = await subscriber.PublishAsync(channel, payload);
        var remoteSubscribers = (int)Math.Max(0L, subscribers - 1);
        return new SyncBackplanePublishResult(Published: true, remoteSubscribers);
    }

    public async Task PeerJoinedAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        cancellationToken.ThrowIfCancellationRequested();

        var member = PresenceMember(connectionId);
        var roomMembers = presenceMembers.GetOrAdd(
            room,
            static _ => new ConcurrentDictionary<Guid, RedisValue>());
        roomMembers[connectionId] = member;
        await AddOrRefreshPresenceMemberAsync(room, member);
    }

    public async Task PeerLeftAsync(string room, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        cancellationToken.ThrowIfCancellationRequested();

        if (presenceMembers.TryGetValue(room, out var roomMembers))
        {
            roomMembers.TryRemove(connectionId, out _);
            if (roomMembers.IsEmpty)
            {
                presenceMembers.TryRemove(room, out _);
            }
        }

        await database.SortedSetRemoveAsync(PresenceKey(room), PresenceMember(connectionId));
    }

    public async Task<int?> GetPeerCountAsync(string room, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        cancellationToken.ThrowIfCancellationRequested();

        var key = PresenceKey(room);
        await database.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, CurrentUnixMilliseconds());
        var count = await database.SortedSetLengthAsync(key);
        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    public async ValueTask DisposeAsync()
    {
        await StopPresenceHeartbeatAsync();
        await connection.CloseAsync();
        await connection.DisposeAsync();
    }

    private string ChannelName(string room)
    {
        return $"{channelPrefix}:{room}";
    }

    private RedisKey PresenceKey(string room)
    {
        return $"{channelPrefix}:presence:{room}";
    }

    private RedisValue PresenceMember(Guid connectionId)
    {
        return $"{instanceId}:{connectionId:N}";
    }

    private Task AddOrRefreshPresenceMemberAsync(string room, RedisValue member)
    {
        var key = PresenceKey(room);
        var score = CurrentUnixMilliseconds() + PresenceTtl.TotalMilliseconds;
        return AddOrRefreshPresenceMembersAsync(key, [new SortedSetEntry(member, score)]);
    }

    private async Task AddOrRefreshPresenceMembersAsync(RedisKey key, SortedSetEntry[] entries)
    {
        await database.SortedSetAddAsync(key, entries);
        await database.KeyExpireAsync(key, PresenceKeyTtl);
    }

    private async Task RunPresenceHeartbeatAsync()
    {
        var cancellationToken = presenceHeartbeatStop.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PresenceHeartbeatInterval, cancellationToken);
                await RefreshPresenceMembersAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                metrics.PresenceTrackerHeartbeatFailed();
                SyncLog.PresenceTrackerHeartbeatFailed(logger, exception);
            }
        }
    }

    private async Task RefreshPresenceMembersAsync()
    {
        var score = CurrentUnixMilliseconds() + PresenceTtl.TotalMilliseconds;
        foreach (var room in presenceMembers)
        {
            var entries = room.Value.Values
                .Select(member => new SortedSetEntry(member, score))
                .ToArray();
            if (entries.Length == 0)
            {
                continue;
            }

            await AddOrRefreshPresenceMembersAsync(PresenceKey(room.Key), entries);
        }
    }

    private async Task StopPresenceHeartbeatAsync()
    {
        presenceHeartbeatStop.Cancel();
        try
        {
            await presenceHeartbeatTask;
        }
        catch (OperationCanceledException)
        {
        }

        presenceHeartbeatStop.Dispose();
    }

    private static double CurrentUnixMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private sealed class RedisSubscription : IDisposable
    {
        private readonly ISubscriber subscriber;
        private readonly RedisChannel channel;

        public RedisSubscription(ISubscriber subscriber, RedisChannel channel)
        {
            this.subscriber = subscriber;
            this.channel = channel;
        }

        public void Dispose()
        {
            subscriber.Unsubscribe(channel);
        }
    }
}
