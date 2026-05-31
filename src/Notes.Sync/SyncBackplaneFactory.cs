using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public static class SyncBackplaneFactory
{
    public static async Task<ISyncBackplane> CreateAsync(SyncServerOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(options.BackplaneRedisConnectionString))
        {
            return NoopSyncBackplane.Instance;
        }

        return await RedisSyncBackplane.ConnectAsync(
            options.BackplaneRedisConnectionString,
            options.BackplaneChannelPrefix,
            logger);
    }
}
