using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Notes.Sync;

public sealed class RedisSyncBackplane : ISyncBackplane, ISyncPresenceTracker, ISyncAdmissionController
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string AdmissionJoinScript = """
        local roomsKey = KEYS[1]
        local roomKey = KEYS[2]
        local room = ARGV[1]
        local member = ARGV[2]
        local expiry = tonumber(ARGV[3])
        local now = tonumber(ARGV[4])
        local maxRooms = tonumber(ARGV[5])
        local maxPeers = tonumber(ARGV[6])
        local ttlSeconds = tonumber(ARGV[7])

        redis.call('ZREMRANGEBYSCORE', roomKey, '-inf', now)
        redis.call('ZREMRANGEBYSCORE', roomsKey, '-inf', now)

        if redis.call('ZSCORE', roomKey, member) then
            redis.call('ZADD', roomKey, expiry, member)
            redis.call('EXPIRE', roomKey, ttlSeconds)
            redis.call('ZADD', roomsKey, expiry, room)
            redis.call('EXPIRE', roomsKey, ttlSeconds)
            return 0
        end

        local peerCount = redis.call('ZCARD', roomKey)
        if peerCount == 0 then
            local roomCount = redis.call('ZCARD', roomsKey)
            if roomCount >= maxRooms then
                return 1
            end
        end

        if peerCount >= maxPeers then
            return 2
        end

        redis.call('ZADD', roomKey, expiry, member)
        redis.call('EXPIRE', roomKey, ttlSeconds)
        redis.call('ZADD', roomsKey, expiry, room)
        redis.call('EXPIRE', roomsKey, ttlSeconds)
        return 0
        """;
    private const string AdmissionLeaveScript = """
        local roomsKey = KEYS[1]
        local roomKey = KEYS[2]
        local room = ARGV[1]
        local member = ARGV[2]
        local now = tonumber(ARGV[3])
        local expiry = tonumber(ARGV[4])
        local ttlSeconds = tonumber(ARGV[5])

        local removed = redis.call('ZREM', roomKey, member)
        redis.call('ZREMRANGEBYSCORE', roomKey, '-inf', now)

        if redis.call('ZCARD', roomKey) == 0 then
            redis.call('DEL', roomKey)
            redis.call('ZREM', roomsKey, room)
        else
            redis.call('ZADD', roomsKey, expiry, room)
            redis.call('EXPIRE', roomKey, ttlSeconds)
            redis.call('EXPIRE', roomsKey, ttlSeconds)
        end

        return removed
        """;
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PresenceHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PresenceKeyTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);
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

    public async Task<SyncBackplaneHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var latency = await database.PingAsync().WaitAsync(HealthCheckTimeout, cancellationToken);
            return SyncBackplaneHealth.Available(latency);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            metrics.BackplaneHealthCheckFailed();
            return SyncBackplaneHealth.Unavailable;
        }
    }

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

    public async Task<SyncJoinResult> TryJoinAsync(
        string room,
        Guid connectionId,
        int maxRooms,
        int maxPeersPerRoom,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRooms);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPeersPerRoom);
        cancellationToken.ThrowIfCancellationRequested();

        var now = CurrentUnixMilliseconds();
        var expiry = now + (long)PresenceTtl.TotalMilliseconds;
        var result = await database.ScriptEvaluateAsync(
            AdmissionJoinScript,
            new RedisKey[] { PresenceRoomsKey(), PresenceKey(room) },
            new RedisValue[]
            {
                room,
                PresenceMember(connectionId),
                expiry,
                now,
                maxRooms,
                maxPeersPerRoom,
                (long)PresenceKeyTtl.TotalSeconds
            });
        return AdmissionResultFromRedis(result);
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

        await RemovePresenceMemberAsync(room, PresenceMember(connectionId));
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

    private RedisKey PresenceRoomsKey()
    {
        return $"{channelPrefix}:presence:rooms";
    }

    private RedisValue PresenceMember(Guid connectionId)
    {
        return $"{instanceId}:{connectionId:N}";
    }

    private Task AddOrRefreshPresenceMemberAsync(string room, RedisValue member)
    {
        var score = CurrentUnixMilliseconds() + PresenceTtl.TotalMilliseconds;
        return AddOrRefreshPresenceMembersAsync(room, [new SortedSetEntry(member, score)], score);
    }

    private async Task AddOrRefreshPresenceMembersAsync(string room, SortedSetEntry[] entries, double roomExpiryScore)
    {
        var key = PresenceKey(room);
        await database.SortedSetAddAsync(key, entries);
        await database.KeyExpireAsync(key, PresenceKeyTtl);
        await database.SortedSetAddAsync(PresenceRoomsKey(), room, roomExpiryScore);
        await database.KeyExpireAsync(PresenceRoomsKey(), PresenceKeyTtl);
    }

    private async Task RemovePresenceMemberAsync(string room, RedisValue member)
    {
        var now = CurrentUnixMilliseconds();
        var expiry = now + (long)PresenceTtl.TotalMilliseconds;
        await database.ScriptEvaluateAsync(
            AdmissionLeaveScript,
            new RedisKey[] { PresenceRoomsKey(), PresenceKey(room) },
            new RedisValue[] { room, member, now, expiry, (long)PresenceKeyTtl.TotalSeconds });
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

            await AddOrRefreshPresenceMembersAsync(room.Key, entries, score);
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

    private static long CurrentUnixMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static SyncJoinResult AdmissionResultFromRedis(RedisResult result)
    {
        return result.ToString() switch
        {
            "0" => SyncJoinResult.Joined,
            "1" => SyncJoinResult.RoomLimitReached,
            "2" => SyncJoinResult.RoomFull,
            var value => throw new InvalidOperationException($"Unexpected Redis admission result '{value}'.")
        };
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
