using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Notes.Sync;

public sealed class RedisSyncBackplane : ISyncBackplane
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConnectionMultiplexer connection;
    private readonly ISubscriber subscriber;
    private readonly string channelPrefix;
    private readonly ILogger logger;

    private RedisSyncBackplane(ConnectionMultiplexer connection, string channelPrefix, ILogger logger)
    {
        this.connection = connection;
        this.subscriber = connection.GetSubscriber();
        this.channelPrefix = channelPrefix.Trim().TrimEnd(':');
        this.logger = logger;
    }

    public bool IsEnabled => true;

    public static async Task<RedisSyncBackplane> ConnectAsync(
        string connectionString,
        string channelPrefix,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelPrefix);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        return new RedisSyncBackplane(connection, channelPrefix, logger);
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
                SyncLog.BackplaneInvalidPayload(logger, exception, room);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
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

    public async ValueTask DisposeAsync()
    {
        await connection.CloseAsync();
        await connection.DisposeAsync();
    }

    private string ChannelName(string room)
    {
        return $"{channelPrefix}:{room}";
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
