using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public delegate Task<ISyncBackplane> SyncRedisBackplaneConnector(
    string connectionString,
    string channelPrefix,
    string instanceId,
    int maxReceiveQueue,
    SyncMetrics metrics,
    ILogger logger);

public static class SyncBackplaneFactory
{
    private static readonly TimeSpan RedisReconnectDelay = TimeSpan.FromSeconds(5);

    public static async Task<ISyncBackplane> CreateAsync(
        SyncServerOptions options,
        SyncMetrics metrics,
        ILogger logger)
    {
        return await CreateAsync(options, metrics, logger, ConnectRedisAsync);
    }

    public static async Task<ISyncBackplane> CreateAsync(
        SyncServerOptions options,
        SyncMetrics metrics,
        ILogger logger,
        SyncRedisBackplaneConnector connectRedisAsync)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connectRedisAsync);

        if (string.IsNullOrWhiteSpace(options.BackplaneRedisConnectionString))
        {
            return NoopSyncBackplane.Instance;
        }

        try
        {
            return await connectRedisAsync(
                options.BackplaneRedisConnectionString,
                options.BackplaneChannelPrefix,
                options.InstanceId,
                options.MaxBackplaneReceiveQueue,
                metrics,
                logger);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            metrics.BackplaneHealthCheckFailed();
            SyncLog.BackplaneConnectFailed(logger, exception);
            return new RecoveringSyncBackplane(
                options.BackplaneRedisConnectionString,
                options.BackplaneChannelPrefix,
                options.InstanceId,
                options.MaxBackplaneReceiveQueue,
                metrics,
                logger,
                connectRedisAsync,
                RedisReconnectDelay,
                startInBackoff: true);
        }
    }

    private static async Task<ISyncBackplane> ConnectRedisAsync(
        string connectionString,
        string channelPrefix,
        string instanceId,
        int maxReceiveQueue,
        SyncMetrics metrics,
        ILogger logger)
    {
        return await RedisSyncBackplane.ConnectAsync(
            connectionString,
            channelPrefix,
            instanceId,
            maxReceiveQueue,
            metrics,
            logger);
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is System.TimeoutException ||
               exception.GetType().FullName is
                   "StackExchange.Redis.RedisConnectionException" or
                   "StackExchange.Redis.RedisTimeoutException";
    }
}
