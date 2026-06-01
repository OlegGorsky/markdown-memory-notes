using Microsoft.Extensions.Logging.Abstractions;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBackplaneFactoryTests
{
    [Fact]
    public async Task CreateAsyncReturnsDegradedBackplaneWhenRedisConnectFails()
    {
        var metrics = new SyncMetrics();
        var options = SyncServerOptions.Default with
        {
            BackplaneRedisConnectionString = "redis.internal:6379"
        };

        await using var backplane = await SyncBackplaneFactory.CreateAsync(
            options,
            metrics,
            NullLogger.Instance,
            static (_, _, _, _, _, _) => throw new TimeoutException("Redis unavailable."));

        var health = await backplane.CheckHealthAsync(CancellationToken.None);

        Assert.True(backplane.IsEnabled);
        Assert.True(health.Enabled);
        Assert.False(health.Healthy);
        Assert.Equal("unavailable", health.Status);
        Assert.Equal(1, metrics.Snapshot().BackplaneHealthCheckFailed);
    }

    [Fact]
    public async Task CreateAsyncDoesNotConnectWhenRedisIsNotConfigured()
    {
        var connected = false;

        await using var backplane = await SyncBackplaneFactory.CreateAsync(
            SyncServerOptions.Default,
            new SyncMetrics(),
            NullLogger.Instance,
            (_, _, _, _, _, _) =>
            {
                connected = true;
                return Task.FromResult<ISyncBackplane>(NoopSyncBackplane.Instance);
            });

        Assert.False(connected);
        Assert.Same(NoopSyncBackplane.Instance, backplane);
    }
}
